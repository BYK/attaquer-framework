namespace AttaquerTaskbar.Models;

public enum MetricLabelMode
{
    Auto,
    Icons,
    Text
}

public enum MetricValueMode
{
    Numbers,
    Sparklines
}

public sealed class TaskbarSettings
{
    public MetricLabelMode MetricLabels { get; set; } = MetricLabelMode.Auto;

    public MetricValueMode MetricValues { get; set; } = MetricValueMode.Numbers;

    public bool ShowWorkspaces { get; set; } = true;

    public bool ShowThermal { get; set; } = true;

    public bool ShowMedia { get; set; } = true;

    public bool AutoTileEnabled { get; set; } = true;
}
