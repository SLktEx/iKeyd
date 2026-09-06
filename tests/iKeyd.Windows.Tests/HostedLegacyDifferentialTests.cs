using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class HostedLegacyDifferentialTests
{
    private const string HostedLegacyTag = "hosted-legacy";
    private const string SendEventTag = "send-event-diff";
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Fact]
    [Trait("Category", "HostedLegacyDifferentialE2E")]
    public async Task Tagged_scenarios_match_the_real_legacy_exe_on_hosted_windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var legacyRunner = new HostedTModeLegacyRunner();
        if (!legacyRunner.IsAvailable)
            return;

        var scenarios = LoadTagged(HostedLegacyTag);
        Assert.NotEmpty(scenarios);

        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            try
            {
                var report = await LegacyDifferentialComparison.RunAsync(
                    scenario,
                    new WindowsScenarioRunner(),
                    legacyRunner);
                var reportPath = LegacyDifferentialComparison.WriteReport(report);

                if (!report.IsMatch)
                    failures.Add(BuildFailureMessage(report, reportPath));
            }
            catch (Exception error)
            {
                failures.Add(
                    $"Hosted differential runner failed for scenario '{scenario.Id}': " +
                    $"{error.GetType().Name}: {error.Message}");
            }
        }

        AssertFailures(failures);
    }

    [Fact]
    [Trait("Category", "HostedLegacyDifferentialE2E")]
    public async Task Reachable_Send_key_events_match_compiled_legacy_exe()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var legacyRunner = new LegacySendEventScenarioRunner();
        if (!legacyRunner.IsAvailable)
            return;

        var scenarios = LoadTagged(SendEventTag);
        Assert.NotEmpty(scenarios);

        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            try
            {
                var report = await LegacyDifferentialComparison.RunAsync(
                    scenario,
                    new IKeydRuntimeScenarioRunner(),
                    legacyRunner);
                var reportPath = LegacyDifferentialComparison.WriteReport(report);
                if (!report.IsMatch)
                    failures.Add(BuildFailureMessage(report, reportPath));
            }
            catch (Exception error)
            {
                failures.Add(
                    $"Compiled Send-event differential failed for scenario '{scenario.Id}': " +
                    $"{error.GetType().Name}: {error.Message}");
            }
        }

        AssertFailures(failures);
    }

    [Fact]
    [Trait("Category", "HostedLegacyDifferentialE2E")]
    public async Task S_Kana_keeps_iKeyd_deterministic_while_accepting_the_observed_compiled_EXE_tail_race()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var legacyRunner = new LegacySendEventScenarioRunner();
        if (!legacyRunner.IsAvailable)
            return;

        var stableEvents = new List<ObservedKeyEvent>
        {
            new() { Kind = "keyDown", Key = "VK_A2" },
            new() { Kind = "keyDown", Key = "Escape" },
            new() { Kind = "keyUp", Key = "Escape" },
            new() { Kind = "keyUp", Key = "VK_A2" }
        };
        var scenario = new CompatibilityScenario
        {
            Id = "runtime-s-kana-known-compiled-tail-race",
            InitialState = new ScenarioInitialState { Mode = "S", Ime = "off", Layers = ["S"] },
            Input =
            [
                new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "KANA" },
                new ScenarioInputEvent { AtMs = 11, Kind = "keyUp", Key = "KANA" }
            ],
            Expected = new ScenarioExpected { Events = stableEvents }
        };

        var iKeyd = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);
        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, iKeyd));

        var legacy = await legacyRunner.RunAsync(scenario);
        var withSpaceTail = stableEvents.Concat([
            new ObservedKeyEvent { Kind = "keyDown", Key = "Space" },
            new ObservedKeyEvent { Kind = "keyUp", Key = "Space" }
        ]).ToArray();

        Assert.True(
            EventSequenceEquals(legacy.Events, stableEvents) || EventSequenceEquals(legacy.Events, withSpaceTail),
            $"Pinned compiled EXE produced an unrecognized S+Kana sequence: {string.Join(", ", legacy.Events.Select(item => $"{item.Kind}:{item.Key}"))}");
    }

    [Fact]
    public void Hosted_adapter_bootstraps_inner_runner_in_S_mode_and_disables_IME_dependency()
    {
        var scenario = new CompatibilityScenario
        {
            Id = "hosted-adapter-contract",
            InitialState = new ScenarioInitialState { Mode = "K", Ime = "on" },
            Input =
            [
                new ScenarioInputEvent { AtMs = 0, Kind = "keyDown", Key = "K" },
                new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "Q" }
            ],
            Expected = new ScenarioExpected { Text = "ti" }
        };

        var adapted = HostedTModeLegacyRunner.PrepareScenario(scenario);

        Assert.Equal("S", adapted.InitialState.Mode);
        Assert.Equal("off", adapted.InitialState.Ime);
        Assert.Equal(1000, adapted.Input[0].AtMs);
        Assert.Equal(1010, adapted.Input[1].AtMs);
        Assert.Equal("K", scenario.InitialState.Mode);
        Assert.Equal("on", scenario.InitialState.Ime);
        Assert.Equal(0, scenario.Input[0].AtMs);
    }

    [Fact]
    public void Hosted_adapter_uses_M3_for_S_and_M4_then_M3_for_K()
    {
        Assert.Equal(new byte[] { 0x33 }, HostedTModeLegacyRunner.ResolveModeSelectionDigits("S"));
        Assert.Equal(new byte[] { 0x34, 0x33 }, HostedTModeLegacyRunner.ResolveModeSelectionDigits("K"));
    }

    [Theory]
    [InlineData(@"C:\runner\hotkeySKG.exe", "hotkeySKG")]
    [InlineData(@"C:\runner\compiled-hotkeySKG.exe", "compiled-hotkeySKG")]
    [InlineData(null, "hotkeySKG")]
    public void Hosted_adapter_tracks_the_configured_legacy_process_name(string? executablePath, string expected)
        => Assert.Equal(expected, HostedTModeLegacyRunner.ResolveLegacyProcessName(executablePath));

    private static bool EventSequenceEquals(
        IReadOnlyList<ObservedKeyEvent> actual,
        IReadOnlyList<ObservedKeyEvent> expected)
    {
        if (actual.Count != expected.Count)
            return false;
        for (var index = 0; index < actual.Count; index++)
        {
            if (!string.Equals(actual[index].Kind, expected[index].Kind, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(actual[index].Key, expected[index].Key, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static CompatibilityScenario[] LoadTagged(string tag)
        => CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Where(item => item.Tags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void AssertFailures(IReadOnlyCollection<string> failures)
        => Assert.True(
            failures.Count == 0,
            failures.Count == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, failures));

    private static string BuildFailureMessage(LegacyDifferentialReport report, string reportPath)
    {
        static string Describe(IReadOnlyList<string> differences)
            => differences.Count == 0 ? "<none>" : string.Join("; ", differences);

        return string.Join(
            Environment.NewLine,
            $"Hosted differential mismatch for scenario '{report.ScenarioId}'.",
            $"iKeyd vs expected: {Describe(report.IKeydVsExpected)}",
            $"legacy vs expected: {Describe(report.LegacyVsExpected)}",
            $"iKeyd vs legacy: {Describe(report.IKeydVsLegacy)}",
            $"report: {reportPath}");
    }
}
