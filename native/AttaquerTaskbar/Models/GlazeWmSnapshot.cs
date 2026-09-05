namespace AttaquerTaskbar.Models;

public sealed record WorkspaceSnapshot(
    string Name,
    string Label,
    bool HasFocus,
    bool IsDisplayed);

public sealed record GlazeWmSnapshot(
    bool IsAvailable,
    IReadOnlyList<WorkspaceSnapshot> Workspaces,
    bool AutoTileEnabled,
    string Status,
    string? AutoTileDirection)
{
    public static GlazeWmSnapshot Empty { get; } = new(
        false,
        Array.Empty<WorkspaceSnapshot>(),
        true,
        "GlazeWM IPC is unavailable",
        null);
}
