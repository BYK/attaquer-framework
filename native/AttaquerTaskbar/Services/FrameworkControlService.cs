using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using AttaquerTaskbar.Models;

namespace AttaquerTaskbar.Services;

public sealed class FrameworkControlService : IDisposable
{
    public const string DashboardUrl = "http://127.0.0.1:30912";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CalibrationRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly Dispatcher _dispatcher;
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:30912/api/"),
        Timeout = TimeSpan.FromMilliseconds(1500)
    };
    private readonly CancellationTokenSource _cancellation = new();
    private FanCalibrationCurve? _calibration;
    private DateTimeOffset _lastCalibrationAttempt = DateTimeOffset.MinValue;
    private Task? _pollTask;
    private int _started;
    private bool _isDisposed;

    public FrameworkControlService(Dispatcher dispatcher) =>
        _dispatcher = dispatcher;

    public ThermalSnapshot CurrentSnapshot { get; private set; } = ThermalSnapshot.Unavailable;

    public event Action<ThermalSnapshot>? StateChanged;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _pollTask = PollAsync(_cancellation.Token);
    }

    public static void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DashboardUrl) { UseShellExecute = true });
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cancellation.Cancel();
        _httpClient.Dispose();
        _cancellation.Dispose();
        StateChanged = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var calibrationRetryInterval = _calibration is null
                    ? TimeSpan.FromSeconds(30)
                    : CalibrationRefreshInterval;
                if (DateTimeOffset.UtcNow - _lastCalibrationAttempt >= calibrationRetryInterval)
                {
                    try { await RefreshCalibrationAsync(cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                    catch { /* Raw RPM remains useful when calibration is unavailable. */ }
                }

                var snapshot = await FetchThermalSnapshotAsync(cancellationToken);
                consecutiveFailures = 0;
                Publish(snapshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                consecutiveFailures++;
                if (consecutiveFailures >= 3) Publish(ThermalSnapshot.Unavailable);
            }

            try { await Task.Delay(PollInterval, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RefreshCalibrationAsync(CancellationToken cancellationToken)
    {
        _lastCalibrationAttempt = DateTimeOffset.UtcNow;

        using var response = await _httpClient.GetAsync("config", cancellationToken);
        if (!response.IsSuccessStatusCode) return;

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var config = await JsonSerializer.DeserializeAsync(
            body,
            FrameworkControlJsonContext.Default.FrameworkConfig,
            cancellationToken);

        var points = config?.Fan?.Calibration?.Points;
        if (points is not null) _calibration = FanCalibrationCurve.Create(points);
    }

    private async Task<ThermalSnapshot> FetchThermalSnapshotAsync(
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("thermal/history", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var samples = await JsonSerializer.DeserializeAsync(
            body,
            FrameworkControlJsonContext.Default.ThermalSampleArray,
            cancellationToken);
        var latest = samples?.LastOrDefault()
            ?? throw new InvalidOperationException("Framework Control returned no thermal samples.");

        var temperature = latest.Temps?.Values
            .Where(double.IsFinite)
            .Select(value => (double?)value)
            .Max();
        var rpm = latest.Rpms?.FirstOrDefault();
        int? fanRpm = latest.Rpms is { Count: > 0 } && rpm is double value && double.IsFinite(value)
            ? (int)Math.Round(value)
            : null;
        int? fanPercent = fanRpm is not null && _calibration is not null
            ? _calibration.ToPercent(fanRpm.Value)
            : null;

        return new ThermalSnapshot(temperature, fanRpm, fanPercent, true);
    }

    private void Publish(ThermalSnapshot snapshot)
    {
        if (_isDisposed || snapshot == CurrentSnapshot) return;
        CurrentSnapshot = snapshot;
        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(snapshot));
    }
}

internal sealed class FanCalibrationCurve
{
    private readonly double[] _rpm;
    private readonly double[] _duty;
    private readonly double[] _b;
    private readonly double[] _c;
    private readonly double[] _d;

    private FanCalibrationCurve(double[] rpm, double[] duty)
    {
        _rpm = rpm;
        _duty = duty;

        var count = rpm.Length;
        var h = new double[count - 1];
        for (var index = 0; index < count - 1; index++) h[index] = rpm[index + 1] - rpm[index];

        var alpha = new double[count];
        for (var index = 1; index < count - 1; index++)
        {
            alpha[index] =
                (3 / h[index]) * (duty[index + 1] - duty[index]) -
                (3 / h[index - 1]) * (duty[index] - duty[index - 1]);
        }

        var l = Enumerable.Repeat(1.0, count).ToArray();
        var mu = new double[count];
        var z = new double[count];

        for (var index = 1; index < count - 1; index++)
        {
            l[index] = 2 * (rpm[index + 1] - rpm[index - 1]) - h[index - 1] * mu[index - 1];
            mu[index] = h[index] / l[index];
            z[index] = (alpha[index] - h[index - 1] * z[index - 1]) / l[index];
        }

        _b = new double[count];
        _c = new double[count];
        _d = new double[count];

        for (var index = count - 2; index >= 0; index--)
        {
            _c[index] = z[index] - mu[index] * _c[index + 1];
            _b[index] =
                (duty[index + 1] - duty[index]) / h[index] -
                h[index] * (_c[index + 1] + 2 * _c[index]) / 3;
            _d[index] = (_c[index + 1] - _c[index]) / (3 * h[index]);
        }
    }

    public static FanCalibrationCurve? Create(IEnumerable<double[]> rawPoints)
    {
        // Framework Control stores [duty %, RPM]. Inverting it can create
        // duplicate RPM values around the fan's stall point, which would make
        // a spline singular, so duplicates are averaged and zero-RPM points
        // are excluded. A stopped fan is handled explicitly in ToPercent.
        var points = rawPoints
            .Where(point => point.Length >= 2 && double.IsFinite(point[0]) && double.IsFinite(point[1]))
            .Select(point => (Duty: point[0], Rpm: point[1]))
            .Where(point => point.Rpm > 0)
            .GroupBy(point => point.Rpm)
            .Select(group => (Rpm: group.Key, Duty: group.Average(point => point.Duty)))
            .OrderBy(point => point.Rpm)
            .ToArray();

        return points.Length < 2
            ? null
            : new FanCalibrationCurve(
                points.Select(point => point.Rpm).ToArray(),
                points.Select(point => point.Duty).ToArray());
    }

    public int ToPercent(int rpm)
    {
        if (rpm <= 0) return 0;
        if (rpm <= _rpm[0]) return ClampAndRound(_duty[0]);
        if (rpm >= _rpm[^1]) return ClampAndRound(_duty[^1]);

        var interval = Array.BinarySearch(_rpm, (double)rpm);
        if (interval >= 0) return ClampAndRound(_duty[interval]);
        interval = ~interval - 1;

        var delta = rpm - _rpm[interval];
        var duty =
            _duty[interval] +
            _b[interval] * delta +
            _c[interval] * delta * delta +
            _d[interval] * delta * delta * delta;
        return ClampAndRound(duty);
    }

    private static int ClampAndRound(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 100));
}

internal sealed class FrameworkConfig
{
    [JsonPropertyName("fan")]
    public FanConfig? Fan { get; init; }
}

internal sealed class FanConfig
{
    [JsonPropertyName("calibration")]
    public FanCalibration? Calibration { get; init; }
}

internal sealed class FanCalibration
{
    [JsonPropertyName("points")]
    public List<double[]>? Points { get; init; }
}

internal sealed class ThermalSample
{
    [JsonPropertyName("temps")]
    public Dictionary<string, double>? Temps { get; init; }

    [JsonPropertyName("rpms")]
    public List<double>? Rpms { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FrameworkConfig))]
[JsonSerializable(typeof(ThermalSample[]))]
internal partial class FrameworkControlJsonContext : JsonSerializerContext;
