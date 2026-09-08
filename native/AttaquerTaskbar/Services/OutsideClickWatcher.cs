using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AttaquerTaskbar.Diagnostics;

namespace AttaquerTaskbar.Services;

/// <summary>
/// Dismisses a non-activating taskbar flyout when a mouse button is pressed in
/// another process. WPF's normal deactivation path is not reliable for a
/// window hosted in the taskbar with ShowActivated=False.
/// </summary>
internal sealed class OutsideClickWatcher
{
    private const int WhMouseLowLevel = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly Dispatcher _dispatcher;
    private readonly Action _dismiss;
    private readonly LowLevelMouseProc _hookProcedure;
    private IntPtr _hook;
    private int _dismissQueued;
    private long _generation;

    internal OutsideClickWatcher(Dispatcher dispatcher, Action dismiss)
    {
        _dispatcher = dispatcher;
        _dismiss = dismiss;
        _hookProcedure = OnMouseEvent;
    }

    internal void Start()
    {
        if (_hook != IntPtr.Zero) return;

        _generation++;
        _hook = SetWindowsHookEx(
            WhMouseLowLevel,
            _hookProcedure,
            GetModuleHandle(null),
            0);
        if (_hook != IntPtr.Zero) return;

        DiagnosticLog.WriteException(
            "Could not start outside-click watcher",
            new Win32Exception(Marshal.GetLastWin32Error()));
    }

    internal void Stop()
    {
        _generation++;
        Interlocked.Exchange(ref _dismissQueued, 0);
        if (_hook == IntPtr.Zero) return;

        var hook = _hook;
        _hook = IntPtr.Zero;
        if (!UnhookWindowsHookEx(hook))
        {
            DiagnosticLog.WriteException(
                "Could not stop outside-click watcher",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && IsButtonDown(message) && ClickTargetsAnotherProcess(data))
                QueueDismissal();
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Outside-click watcher ignored an input error", exception);
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private static bool IsButtonDown(IntPtr message) =>
        message.ToInt64() is
            WmLeftButtonDown or
            WmRightButtonDown or
            WmMiddleButtonDown or
            WmXButtonDown;

    private static bool ClickTargetsAnotherProcess(IntPtr data)
    {
        var mouse = Marshal.PtrToStructure<LowLevelMouseData>(data);
        var target = WindowFromPoint(mouse.Point);
        if (target == IntPtr.Zero) return true;

        _ = GetWindowThreadProcessId(target, out var processId);
        return processId != (uint)Environment.ProcessId;
    }

    private void QueueDismissal()
    {
        if (Interlocked.Exchange(ref _dismissQueued, 1) != 0) return;

        var generation = _generation;
        _ = _dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                Interlocked.Exchange(ref _dismissQueued, 0);
                if (_hook != IntPtr.Zero && generation == _generation) _dismiss();
            }));
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        internal readonly int X;
        internal readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelMouseData
    {
        internal readonly Point Point;
        internal readonly uint MouseData;
        internal readonly uint Flags;
        internal readonly uint Time;
        internal readonly UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProc hookProcedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
