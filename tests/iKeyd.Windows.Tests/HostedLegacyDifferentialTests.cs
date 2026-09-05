using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class HostedLegacyDifferentialTests
{
    private const string HostedLegacyTag = "hosted-legacy";
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

        var scenarios = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Where(item => item.Tags.Any(tag =>
                string.Equals(tag, HostedLegacyTag, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

        Assert.True(
            failures.Count == 0,
            failures.Count == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    [Fact]
    public void Hosted_adapter_keeps_S_keymap_but_disables_IME_dependency()
    {
        var scenario = new CompatibilityScenario
        {
            Id = "hosted-adapter-contract",
            InitialState = new ScenarioInitialState { Mode = "S", Ime = "on" },
            Input =
            [
                new ScenarioInputEvent { AtMs = 0, Kind = "keyDown", Key = "K" },
                new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "Q" }
            ],
            Expected = new ScenarioExpected { Text = "fa" }
        };

        var adapted = HostedTModeLegacyRunner.PrepareScenario(scenario);

        Assert.Equal("S", adapted.InitialState.Mode);
        Assert.Equal("off", adapted.InitialState.Ime);
        Assert.Equal(500, adapted.Input[0].AtMs);
        Assert.Equal(510, adapted.Input[1].AtMs);
        Assert.Equal("on", scenario.InitialState.Ime);
        Assert.Equal(0, scenario.Input[0].AtMs);
    }

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
