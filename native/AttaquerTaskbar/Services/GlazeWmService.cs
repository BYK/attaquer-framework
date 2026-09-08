using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using AttaquerTaskbar.Diagnostics;
using AttaquerTaskbar.Models;

namespace AttaquerTaskbar.Services;

public sealed class GlazeWmService : IDisposable
{
    private static readonly Uri IpcEndpoint = new("ws://127.0.0.1:6123");
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    private const string SubscriptionRequest =
        "sub -e focus_changed focused_container_moved window_managed window_unmanaged " +
        "workspace_activated workspace_deactivated workspace_updated " +
        "tiling_direction_changed application_exiting";

    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _tilingGate = new(1, 1);
    private readonly object _connectionLock = new();
    private readonly object _pendingLock = new();
    private readonly object _debounceLock = new();
    private readonly object _tilingStateLock = new();
    private readonly Dictionary<string, string> _lastDirectionByWindow = new();

    private ClientWebSocket? _activeSocket;
    private PendingRequest? _pendingRequest;
    private CancellationTokenSource? _workspaceRefreshDebounce;
    private CancellationTokenSource? _tilingDebounce;
    private string? _knownTilingDirection;
    private string? _lastFocusedWindowId;
    private bool _started;
    private bool _disposed;
    private bool _unavailableLogged;

    public GlazeWmService(Dispatcher dispatcher, SettingsService settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
    }

    public GlazeWmSnapshot CurrentSnapshot { get; private set; } = GlazeWmSnapshot.Empty;

    public event Action<GlazeWmSnapshot>? StateChanged;

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _settings.Changed += OnSettingsChanged;
        _ = RunConnectionLoopAsync(_lifetime.Token);
    }

    public async Task FocusWorkspaceAsync(string workspaceName)
    {
        if (_disposed || string.IsNullOrWhiteSpace(workspaceName)) return;
        try
        {
            await SendRequestAsync(
                $"command focus --workspace {EscapeCommandArgument(workspaceName)}",
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("GlazeWM workspace focus command failed", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _lifetime.Cancel();

        lock (_debounceLock)
        {
            CancelAndDispose(ref _workspaceRefreshDebounce);
            CancelAndDispose(ref _tilingDebounce);
        }

        lock (_connectionLock) _activeSocket?.Abort();
        FailPending(new OperationCanceledException("GlazeWM service stopped."));
        StateChanged = null;
    }

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        var retryAttempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = CreateSocket();
            using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task? receiveTask = null;

            try
            {
                await socket.ConnectAsync(IpcEndpoint, cancellationToken);
                lock (_connectionLock) _activeSocket = socket;

                receiveTask = ReceiveLoopAsync(socket, connectionLifetime.Token);
                await SendRequestAsync(SubscriptionRequest, connectionLifetime.Token);

                retryAttempt = 0;
                _unavailableLogged = false;
                DiagnosticLog.Write("GlazeWM IPC connected; event-driven auto tiler active.");
                ScheduleWorkspaceRefresh(immediate: true);
                _ = RefreshKnownDirectionAsync(connectionLifetime.Token);

                await receiveTask;
                PublishUnavailable("GlazeWM is restarting");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PublishUnavailable("GlazeWM IPC is unavailable");
                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    DiagnosticLog.WriteException("GlazeWM IPC connection unavailable", exception);
                }
            }
            finally
            {
                lock (_connectionLock)
                {
                    if (ReferenceEquals(_activeSocket, socket)) _activeSocket = null;
                }

                connectionLifetime.Cancel();
                socket.Abort();
                FailPending(new IOException("GlazeWM IPC connection closed."));
                if (receiveTask is not null && !receiveTask.IsCompleted)
                {
                    try { await receiveTask; }
                    catch { }
                }
            }

            if (cancellationToken.IsCancellationRequested) break;
            var delay = ReconnectDelays[Math.Min(retryAttempt, ReconnectDelays.Length - 1)];
            retryAttempt++;
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await ReceiveTextAsync(socket, cancellationToken);
            if (message is null) return;

            try
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (TryReadString(root, "messageType", out var messageType) &&
                    messageType == "client_response")
                {
                    CompletePendingRequest(root);
                    continue;
                }

                if (!TryReadString(root, "messageType", out messageType) ||
                    messageType != "event_subscription")
                    continue;

                if (root.TryGetProperty("success", out var success) && !success.GetBoolean())
                {
                    var error = TryReadString(root, "error", out var errorText)
                        ? errorText
                        : "GlazeWM event subscription failed.";
                    throw new InvalidOperationException(error);
                }

                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    continue;
                if (HandleEvent(data.Clone(), cancellationToken)) return;
            }
            catch (JsonException exception)
            {
                DiagnosticLog.WriteException("Ignored invalid GlazeWM IPC message", exception);
            }
        }
    }

    private bool HandleEvent(JsonElement data, CancellationToken cancellationToken)
    {
        if (!TryReadString(data, "eventType", out var eventType)) return false;

        switch (eventType)
        {
            case "application_exiting":
                DiagnosticLog.Write("GlazeWM reported application exit; waiting for restart.");
                return true;

            case "tiling_direction_changed":
                if (TryReadString(data, "newTilingDirection", out var changedDirection) &&
                    GlazeWmTilingPolicy.IsDirection(changedDirection))
                {
                    lock (_tilingStateLock) _knownTilingDirection = changedDirection;
                    PublishDirection(changedDirection);
                }
                break;

            case "focus_changed":
                EvaluateEventContainer(data, "focusedContainer", force: false, eventType, cancellationToken);
                break;

            case "focused_container_moved":
                EvaluateEventContainer(data, "focusedContainer", force: true, eventType, cancellationToken);
                ScheduleWorkspaceRefresh(immediate: false);
                break;

            case "window_managed":
                EvaluateEventContainer(data, "managedWindow", force: true, eventType, cancellationToken);
                ScheduleWorkspaceRefresh(immediate: false);
                break;

            case "workspace_activated":
                EvaluateEventContainer(data, "activatedWorkspace", force: true, eventType, cancellationToken);
                ScheduleWorkspaceRefresh(immediate: false);
                break;

            case "workspace_updated":
                if (data.TryGetProperty("updatedWorkspace", out var updatedWorkspace) &&
                    updatedWorkspace.ValueKind == JsonValueKind.Object &&
                    ReadBoolean(updatedWorkspace, "hasFocus"))
                    ScheduleTilingEvaluation(updatedWorkspace.Clone());
                ScheduleWorkspaceRefresh(immediate: false);
                break;

            case "workspace_deactivated":
            case "window_unmanaged":
                ScheduleWorkspaceRefresh(immediate: false);
                break;
        }

        return false;
    }

    private void EvaluateEventContainer(
        JsonElement eventData,
        string propertyName,
        bool force,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!eventData.TryGetProperty(propertyName, out var container) ||
            container.ValueKind != JsonValueKind.Object)
            return;
        _ = EvaluateTilingAsync(container.Clone(), force, reason, cancellationToken);
    }

    private void ScheduleWorkspaceRefresh(bool immediate)
    {
        if (_disposed) return;
        CancellationToken token;
        lock (_debounceLock)
        {
            CancelAndDispose(ref _workspaceRefreshDebounce);
            _workspaceRefreshDebounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _workspaceRefreshDebounce.Token;
        }

        _ = RefreshWorkspacesAfterDelayAsync(immediate, token);
    }

    private async Task RefreshWorkspacesAfterDelayAsync(bool immediate, CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate) await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken);
            var data = await SendRequestAsync("query workspaces", cancellationToken);
            if (data is not JsonElement responseData ||
                !responseData.TryGetProperty("workspaces", out var workspacesElement) ||
                workspacesElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GlazeWM returned no workspace list.");

            var workspaceElements = workspacesElement.EnumerateArray().Select(element => element.Clone()).ToArray();
            var workspaces = workspaceElements
                .Select(ParseWorkspace)
                .Where(workspace => workspace is not null)
                .Cast<WorkspaceSnapshot>()
                .ToArray();
            string? direction;
            lock (_tilingStateLock) direction = _knownTilingDirection;

            Publish(new GlazeWmSnapshot(
                true,
                workspaces,
                _settings.Current.AutoTileEnabled,
                workspaces.Length == 0 ? "No active GlazeWM workspaces" : "GlazeWM connected",
                direction));

            if (_settings.Current.AutoTileEnabled && string.IsNullOrWhiteSpace(_lastFocusedWindowId))
            {
                foreach (var workspace in workspaceElements)
                {
                    if (!ReadBoolean(workspace, "hasFocus")) continue;
                    _ = EvaluateTilingAsync(workspace, force: true, "initial state", cancellationToken);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("GlazeWM workspace refresh failed", exception);
        }
    }

    private void ScheduleTilingEvaluation(JsonElement container)
    {
        if (_disposed || !_settings.Current.AutoTileEnabled) return;
        CancellationToken token;
        lock (_debounceLock)
        {
            CancelAndDispose(ref _tilingDebounce);
            _tilingDebounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _tilingDebounce.Token;
        }

        _ = EvaluateTilingAfterDelayAsync(container, token);
    }

    private async Task EvaluateTilingAfterDelayAsync(JsonElement container, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(160), cancellationToken);
            await EvaluateTilingAsync(container, force: false, "settled resize", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task EvaluateTilingAsync(
        JsonElement container,
        bool force,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_disposed || !_settings.Current.AutoTileEnabled) return;
        if (!TryFindFocusedTiledWindow(container, out var windowId, out var width, out var height)) return;

        await _tilingGate.WaitAsync(cancellationToken);
        try
        {
            string? currentDirection;
            string? lastFocusedWindow;
            string? cachedDirection;
            lock (_tilingStateLock)
            {
                currentDirection = _knownTilingDirection;
                lastFocusedWindow = _lastFocusedWindowId;
                _lastDirectionByWindow.TryGetValue(windowId, out cachedDirection);
            }

            var direction = GlazeWmTilingPolicy.DirectionForSize(width, height, currentDirection);
            if (direction is null) return;
            if (!force && lastFocusedWindow == windowId &&
                cachedDirection == direction && currentDirection == direction)
                return;

            await SendRequestAsync($"command set-tiling-direction {direction}", cancellationToken);
            lock (_tilingStateLock)
            {
                _knownTilingDirection = direction;
                _lastFocusedWindowId = windowId;
                _lastDirectionByWindow[windowId] = direction;
                if (_lastDirectionByWindow.Count > 1000) _lastDirectionByWindow.Clear();
            }

            DiagnosticLog.Write(
                $"GlazeWM auto tiler set {direction} for {width:0} x {height:0} window ({reason}).");
            PublishDirection(direction);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("GlazeWM automatic tiling command failed", exception);
        }
        finally
        {
            _tilingGate.Release();
        }
    }

    private async Task RefreshKnownDirectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var data = await SendRequestAsync("query tiling-direction", cancellationToken);
            if (data is JsonElement responseData &&
                TryReadString(responseData, "tilingDirection", out var direction) &&
                GlazeWmTilingPolicy.IsDirection(direction))
            {
                lock (_tilingStateLock) _knownTilingDirection = direction;
                PublishDirection(direction);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("GlazeWM tiling-direction query failed", exception);
        }
    }

    private async Task<JsonElement?> SendRequestAsync(string request, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        PendingRequest? pending = null;
        ClientWebSocket socket;
        try
        {
            lock (_connectionLock)
            {
                var connected = _activeSocket;
                if (connected is null || connected.State != WebSocketState.Open)
                    throw new IOException("GlazeWM IPC is not connected.");
                socket = connected;
            }

            pending = new PendingRequest(request);
            lock (_pendingLock) _pendingRequest = pending;
            await SendTextAsync(socket, request, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                return await pending.Completion.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Abort();
                throw new TimeoutException($"GlazeWM did not answer `{request}`.");
            }
        }
        finally
        {
            if (pending is not null)
            {
                lock (_pendingLock)
                {
                    if (ReferenceEquals(_pendingRequest, pending)) _pendingRequest = null;
                }
            }

            _requestGate.Release();
        }
    }

    private void CompletePendingRequest(JsonElement root)
    {
        if (!TryReadString(root, "clientMessage", out var clientMessage)) return;
        PendingRequest? pending;
        lock (_pendingLock) pending = _pendingRequest;
        if (pending is null || pending.Message != clientMessage) return;

        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            var error = TryReadString(root, "error", out var errorText)
                ? errorText
                : "GlazeWM rejected the request.";
            pending.Completion.TrySetException(new InvalidOperationException(error));
            return;
        }

        pending.Completion.TrySetResult(
            root.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null
                ? data.Clone()
                : null);
    }

    private void FailPending(Exception exception)
    {
        PendingRequest? pending;
        lock (_pendingLock)
        {
            pending = _pendingRequest;
            _pendingRequest = null;
        }

        pending?.Completion.TrySetException(exception);
    }

    private void OnSettingsChanged(TaskbarSettings settings)
    {
        if (!settings.AutoTileEnabled)
        {
            lock (_debounceLock) CancelAndDispose(ref _tilingDebounce);
            lock (_tilingStateLock)
            {
                _lastFocusedWindowId = null;
                _lastDirectionByWindow.Clear();
            }
        }

        var snapshot = CurrentSnapshot;
        Publish(snapshot with
        {
            AutoTileEnabled = settings.AutoTileEnabled,
            AutoTileDirection = settings.AutoTileEnabled ? snapshot.AutoTileDirection : null
        });
        ScheduleWorkspaceRefresh(immediate: true);
    }

    private void PublishUnavailable(string status)
    {
        if (!CurrentSnapshot.IsAvailable && CurrentSnapshot.Status == status) return;
        Publish(new GlazeWmSnapshot(
            false,
            Array.Empty<WorkspaceSnapshot>(),
            _settings.Current.AutoTileEnabled,
            status,
            null));
    }

    private void PublishDirection(string direction)
    {
        var snapshot = CurrentSnapshot;
        Publish(snapshot with
        {
            IsAvailable = true,
            AutoTileEnabled = _settings.Current.AutoTileEnabled,
            AutoTileDirection = _settings.Current.AutoTileEnabled ? direction : null,
            Status = "GlazeWM connected"
        });
    }

    private void Publish(GlazeWmSnapshot snapshot)
    {
        if (_disposed) return;
        CurrentSnapshot = snapshot;
        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(snapshot));
    }

    private static WorkspaceSnapshot? ParseWorkspace(JsonElement element)
    {
        if (!TryReadString(element, "name", out var name) || string.IsNullOrWhiteSpace(name)) return null;
        var label = TryReadString(element, "displayName", out var displayName) &&
                    !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : name;
        return new WorkspaceSnapshot(
            name,
            label,
            ReadBoolean(element, "hasFocus"),
            ReadBoolean(element, "isDisplayed"));
    }

    private static bool TryFindFocusedTiledWindow(
        JsonElement container,
        out string windowId,
        out double width,
        out double height)
    {
        if (TryReadString(container, "type", out var type) && type == "window" &&
            ReadBoolean(container, "hasFocus") && IsTilingWindow(container))
        {
            windowId = TryReadString(container, "id", out var id) ? id : string.Empty;
            width = ReadNumber(container, "width");
            height = ReadNumber(container, "height");
            return !string.IsNullOrWhiteSpace(windowId) && width > 0 && height > 0;
        }

        if (container.TryGetProperty("children", out var children) &&
            children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                if (TryFindFocusedTiledWindow(child, out windowId, out width, out height)) return true;
        }

        windowId = string.Empty;
        width = height = 0;
        return false;
    }

    private static bool IsTilingWindow(JsonElement window)
    {
        if (!window.TryGetProperty("state", out var state)) return false;
        if (state.ValueKind == JsonValueKind.String) return state.GetString() == "tiling";
        return state.ValueKind == JsonValueKind.Object &&
               TryReadString(state, "type", out var type) && type == "tiling";
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        property.GetBoolean();

    private static double ReadNumber(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? value
            : 0;

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        return socket;
    }

    private async Task SendTextAsync(
        ClientWebSocket socket,
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private static async Task<string?> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var output = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(output.ToArray());
        }
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }

    private static string EscapeCommandArgument(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"') || value.Contains('\\')
            ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : value;

    private sealed class PendingRequest
    {
        public PendingRequest(string message)
        {
            Message = message;
            Completion = new TaskCompletionSource<JsonElement?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string Message { get; }

        public TaskCompletionSource<JsonElement?> Completion { get; }
    }
}
