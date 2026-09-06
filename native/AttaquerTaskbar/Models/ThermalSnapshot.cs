namespace AttaquerTaskbar.Models;

public sealed record ThermalSnapshot(
    double? TemperatureCelsius,
    int? FanRpm,
    int? FanPercent,
    bool IsAvailable)
{
    public static ThermalSnapshot Unavailable { get; } = new(null, null, null, false);
}
