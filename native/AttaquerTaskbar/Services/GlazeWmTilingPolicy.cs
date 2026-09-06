namespace AttaquerTaskbar.Services;

internal static class GlazeWmTilingPolicy
{
    internal const double DefaultDeadband = 0.10;

    internal static string? DirectionForSize(
        double width,
        double height,
        string? currentDirection,
        double deadband = DefaultDeadband)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return null;
        if (!double.IsFinite(deadband) || deadband < 0) return null;

        if (width > height * (1 + deadband)) return "horizontal";
        if (height > width * (1 + deadband)) return "vertical";
        return IsDirection(currentDirection) ? currentDirection : null;
    }

    internal static bool IsDirection(string? direction) =>
        direction is "horizontal" or "vertical";
}
