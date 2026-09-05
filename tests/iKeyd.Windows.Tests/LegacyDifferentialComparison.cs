using System.Text.Json;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

public sealed record LegacyDifferentialReport
{
    public required string ScenarioId { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required ScenarioInitialState InitialState { get; init; }
    public required IReadOnlyList<ScenarioInputEvent> Input { get; init; }
    public required ScenarioExpected Expected { get; init; }
    public required ScenarioRunResult IKeyd { get; init; }
    public required ScenarioRunResult LegacyExe { get; init; }
    public required IReadOnlyList<string> IKeydVsExpected { get; init; }
    public required IReadOnlyList<string> LegacyVsExpected { get; init; }
    public required IReadOnlyList<string> IKeydVsLegacy { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> InventoryIds { get; init; } = [];
    public IReadOnlyList<string> RequiredEnvironment { get; init; } = [];
    public IReadOnlyList<string> OracleTargets { get; init; } = [];

    public bool IsMatch =>
        IKeydVsExpected.Count == 0 &&
        LegacyVsExpected.Count == 0 &&
        IKeydVsLegacy.Count == 0;
}

public static class LegacyDifferentialComparison
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<LegacyDifferentialReport> RunAsync(
        CompatibilityScenario scenario,
        ICompatibilityScenarioRunner iKeydRunner,
        ICompatibilityScenarioRunner legacyRunner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(iKeydRunner);
        ArgumentNullException.ThrowIfNull(legacyRunner);

        var iKeydBefore = WindowsExecutionSnapshot.Capture(scenario.InitialState.Modifiers);
        var iKeyd = await iKeydRunner.RunAsync(scenario, cancellationToken);
        var iKeydAfter = WindowsExecutionSnapshot.Capture(scenario.InitialState.Modifiers);
        iKeyd = AddExecutionDiagnostics(iKeyd, iKeydBefore, iKeydAfter);

        var legacyBefore = WindowsExecutionSnapshot.Capture(scenario.InitialState.Modifiers);
        var legacy = await legacyRunner.RunAsync(scenario, cancellationToken);
        var legacyAfter = WindowsExecutionSnapshot.Capture(scenario.InitialState.Modifiers);
        legacy = AddExecutionDiagnostics(legacy, legacyBefore, legacyAfter);

        return new LegacyDifferentialReport
        {
            ScenarioId = scenario.Id,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            InitialState = scenario.InitialState,
            Input = scenario.Input.ToArray(),
            Expected = scenario.Expected,
            IKeyd = iKeyd,
            LegacyExe = legacy,
            IKeydVsExpected = CompatibilityScenarioDiff.Compare(scenario, iKeyd),
            LegacyVsExpected = CompatibilityScenarioDiff.Compare(scenario, legacy),
            IKeydVsLegacy = CompareResults(iKeyd, legacy),
            Tags = scenario.Tags.ToArray(),
            InventoryIds = scenario.InventoryIds.ToArray(),
            RequiredEnvironment = scenario.RequiredEnvironment.ToArray(),
            OracleTargets = scenario.OracleTargets.ToArray()
        };
    }

    public static IReadOnlyList<string> CompareResults(
        ScenarioRunResult left,
        ScenarioRunResult right)
    {
        var differences = new List<string>();

        if (!string.Equals(left.ScenarioId, right.ScenarioId, StringComparison.OrdinalIgnoreCase))
            differences.Add($"scenario id: {left.Runner}='{left.ScenarioId}', {right.Runner}='{right.ScenarioId}'");

        if (!string.Equals(left.Text, right.Text, StringComparison.Ordinal))
            differences.Add($"text: {left.Runner}='{left.Text ?? "<null>"}', {right.Runner}='{right.Text ?? "<null>"}'");

        if (left.Events.Count != right.Events.Count)
        {
            differences.Add($"event count: {left.Runner}={left.Events.Count}, {right.Runner}={right.Events.Count}");
            return differences;
        }

        for (var i = 0; i < left.Events.Count; i++)
        {
            var a = left.Events[i];
            var b = right.Events[i];
            if (!string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add(
                    $"event[{i}]: {left.Runner}={a.Kind}:{a.Key}, {right.Runner}={b.Kind}:{b.Key}");
            }
        }

        return differences;
    }

    public static string WriteReport(LegacyDifferentialReport report, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        directory ??= Environment.GetEnvironmentVariable("IKEYD_DIFFERENTIAL_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(AppContext.BaseDirectory, "DifferentialReports");

        Directory.CreateDirectory(directory);
        var safeId = string.Concat(report.ScenarioId.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        var path = Path.Combine(directory, $"{safeId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    private static ScenarioRunResult AddExecutionDiagnostics(
        ScenarioRunResult result,
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var metadata = new Dictionary<string, string>(result.Metadata);
        foreach (var (key, value) in before)
            metadata[$"before.{key}"] = value;
        foreach (var (key, value) in after)
            metadata[$"after.{key}"] = value;

        return result with { Metadata = metadata };
    }
}
