using AttaquerTaskbar.Services;

var cases = new[]
{
    new TestCase("wide window", 1200, 700, null, "horizontal"),
    new TestCase("tall window", 700, 1200, null, "vertical"),
    new TestCase("square without history", 800, 800, null, null),
    new TestCase("near-square keeps horizontal", 1050, 1000, "horizontal", "horizontal"),
    new TestCase("near-square keeps vertical", 1000, 1050, "vertical", "vertical"),
    new TestCase("crosses horizontal deadband", 1110, 1000, "vertical", "horizontal"),
    new TestCase("crosses vertical deadband", 1000, 1110, "horizontal", "vertical"),
    new TestCase("invalid width", 0, 800, "horizontal", null),
    new TestCase("invalid height", 800, double.NaN, "vertical", null)
};

var failures = 0;
foreach (var test in cases)
{
    var actual = GlazeWmTilingPolicy.DirectionForSize(
        test.Width,
        test.Height,
        test.CurrentDirection);
    if (actual == test.ExpectedDirection) continue;

    Console.Error.WriteLine(
        $"FAIL {test.Name}: expected {test.ExpectedDirection ?? "<none>"}, got {actual ?? "<none>"}");
    failures++;
}

if (failures > 0) return 1;
Console.WriteLine($"Passed {cases.Length} auto-tiling policy tests.");
return 0;

internal sealed record TestCase(
    string Name,
    double Width,
    double Height,
    string? CurrentDirection,
    string? ExpectedDirection);
