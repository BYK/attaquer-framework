using System.Runtime.InteropServices;

namespace AttaquerTaskbar.Diagnostics;

internal static class DiagnosticLog
{
    private static readonly object SyncRoot = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AttaquerTaskbar",
        "attaquer-taskbar.log");

    public static void Initialize()
    {
        Write(string.Empty);
        Write("=== Attaquer Taskbar starting ===");
        Write($"Process ID: {Environment.ProcessId}");
        Write($"OS: {RuntimeInformation.OSDescription} ({Environment.OSVersion.Version})");
        Write($"Architecture: {RuntimeInformation.ProcessArchitecture}");
    }

    public static void Write(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never prevent the taskbar process from starting.
        }
    }

    public static void WriteException(string context, Exception exception) =>
        Write($"{context} (HRESULT 0x{exception.HResult:X8}): {exception}");
}
