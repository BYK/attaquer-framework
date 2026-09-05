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
    private const string SubscriptionRequest =
        "sub -e window_managed window_unmanaged focus_changed " +
        "focused_container_moved workspace_activated workspace_deactivated workspace_updated";

    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _debounceLock = new();
    private readonly Dictionary<string, string> _lastDirectionByWindow = new();
    private CancellationTokenSource? _refreshDebounce;
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
        _ = RunSubscriptionLoopAsync(_lifetime.Token);
    }

    public async Task FocusWorkspaceAsync(string workspaceName)
    {
        if (_disposed || string.IsNullOrWhiteSpace(workspaceName)) return;
        try
        {
            await RequestAsync(
                $"command focus --workspace {EscapeCommandArgument(workspaceName)}",
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("GlazeWM workspace focus command failed", exception);
            PublishUnavailable("GlazeWM command failed");
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
            _refreshDebounce?.Cancel();
            _refreshDebounce?.Dispose();
            _refreshDebounce = null;
        }

        _lifetime.Dispose();
        StateChanged = null;
    }

    private async Task RunSubscriptionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = CreateSocket();
                await socket.ConnectAsync(IpcEndpoint, cancellationToken);
                await SendTextAsync(socket, SubscriptionRequest, cancellationToken);
                _unavailableLogged = false;
                ScheduleRefresh(immediate: true);

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var message = await ReceiveTextAsync(socket, cancellationToken);
                    if (message is null) break;

                    using var document = JsonDocument.Parse(message);
                    var root = document.RootElement;
                    if (root.TryGetProperty("messageType", out var messageType) &&
                        messageType.GetString() == "client_response" &&
                        root.TryGetProperty("success", out var success) &&
                        !success.GetBoolean())
                    {
                        var error = root.TryGetProperty("error", out var errorElement)
                            ? errorElement.GetString()
                            : "subscription rejected";
                        throw new InvalidOperationException(error);
                    }

                    ScheduleRefresh(immediate: false);
                }
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

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ScheduleRefresh(bool immediate)
    {
        if (_disposed) return;
        CancellationToken token;
        lock (_debounceLock)
        {
            _refreshDebounce?.Cancel();
            _refreshDebounce?.Dispose();
            _refreshDebounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _refreshDebounce.Token;
        }

        _ = DebouncedRefreshAsync(immediate, token);
    }

    private async Task DebouncedRefreshAsync(bool immediate, CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate) await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken);
            var data = await RequestAsync("query workspaces", cancellationToken);
            if (data is not JsonElement responseData ||
                !responseData.TryGetProperty("workspaces", out var workspacesElement))
                throw new InvalidDataException("GlazeWM returned no workspace list.");

            var workspaceElements = workspacesElement.EnumerateArray().Select(element => element.Clone()).ToArray();
            var workspaces = workspaceElements
                .Select(ParseWorkspace)
                .Where(workspace => workspace is not null)
                .Cast<WorkspaceSnapshot>()
                .ToArray();

            string? direction = null;
            if (_settings.Current.AutoTileEnabled &&
                TryFindFocusedTiledWindow(workspaceElements, out var windowId, out var width, out var height))
            {
                direction = width > height ? "horizontal" : "vertical";
                if (!_lastDirectionByWindow.TryGetValue(windowId, out var previous) || previous != direction)
                {
                    await RequestAsync($"command set-tiling-direction {direction}", cancellationToken);
                    _lastDirectionByWindow[windowId] = direction;
                    if (_lastDirectionByWindow.Count > 1000) _lastDirectionByWindow.Clear();
                }
            }

            Publish(new GlazeWmSnapshot(
                true,
                workspaces,
                _settings.Current.AutoTileEnabled,
                workspaces.Length == 0 ? "No active GlazeWM workspaces" : "GlazeWM connected",
                direction));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishUnavailable("GlazeWM state refresh failed");
            if (!_unavailableLogged)
            {
                _unavailableLogged = true;
                DiagnosticLog.WriteException("GlazeWM workspace refresh failed", exception);
            }
        }
    }

    private async Task<JsonElement?> RequestAsync(string request, CancellationToken cancellationToken)
    {
        using var socket = CreateSocket();
        await socket.ConnectAsync(IpcEndpoint, cancellationToken);
        await SendTextAsync(socket, request, cancellationToken);

        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveTextAsync(socket, cancellationToken);
            if (message is null) return null;
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("messageType", out var messageType) ||
                messageType.GetString() != "client_response" ||
                !root.TryGetProperty("clientMessage", out var clientMessage) ||
                clientMessage.GetString() != request)
                continue;

            if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
            {
                var error = root.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetString()
                    : "GlazeWM rejected the request.";
                throw new InvalidOperationException(error);
            }

            return root.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null
                ? data.Clone()
                : null;
        }

        return null;
    }

    private void OnSettingsChanged(TaskbarSettings settings)
    {
        if (!settings.AutoTileEnabled) _lastDirectionByWindow.Clear();
        ScheduleRefresh(immediate: true);
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
        IEnumerable<JsonElement> workspaces,
        out string windowId,
        out int width,
        out int height)
    {
        foreach (var workspace in workspaces)
        {
            if (!ReadBoolean(workspace, "hasFocus")) continue;
            if (TryFindFocusedTiledWindow(workspace, out windowId, out width, out height)) return true;
        }

        windowId = string.Empty;
        width = height = 0;
        return false;
    }

    private static bool TryFindFocusedTiledWindow(
        JsonElement container,
        out string windowId,
        out int width,
        out int height)
    {
        if (TryReadString(container, "type", out var type) && type == "window" &&
            ReadBoolean(container, "hasFocus") && IsTilingWindow(container))
        {
            windowId = TryReadString(container, "id", out var id) ? id : string.Empty;
            width = ReadInteger(container, "width");
            height = ReadInteger(container, "height");
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

    private static int ReadInteger(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        return socket;
    }

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
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

    private static string EscapeCommandArgument(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"') || value.Contains('\\')
            ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : value;
}
