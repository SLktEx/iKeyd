using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class HostedLegacyDifferentialTests
{
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Theory]
    [Trait("Category", "HostedLegacyDifferentialE2E")]
    [InlineData("s-chord-k-q-immediate")]
    [InlineData("s-k-q-after-100ms")]
    public async Task IKeyd_and_legacy_exe_match_on_hosted_windows_without_IME(string scenarioId)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var legacyRunner = new HostedTModeLegacyRunner();
        if (!legacyRunner.IsAvailable)
            return;

        var scenario = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Single(item => item.Id == scenarioId);

        var report = await LegacyDifferentialComparison.RunAsync(
            scenario,
            new WindowsScenarioRunner(),
            legacyRunner);
        var reportPath = LegacyDifferentialComparison.WriteReport(report);

        Assert.True(report.IsMatch, BuildFailureMessage(report, reportPath));
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
