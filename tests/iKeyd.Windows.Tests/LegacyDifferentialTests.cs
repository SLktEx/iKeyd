using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyDifferentialTests
{
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Theory]
    [Trait("Category", "LegacyDifferentialE2E")]
    [InlineData("s-chord-k-q-immediate")]
    [InlineData("s-k-q-after-100ms")]
    public async Task IKeyd_and_legacy_exe_produce_the_same_observable_output(string scenarioId)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var legacyRunner = new LegacyExecutableScenarioRunner();
        if (!legacyRunner.IsAvailable)
            return;

        var scenario = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Single(item => item.Id == scenarioId);

        var report = await LegacyDifferentialComparison.RunAsync(
            scenario,
            new WindowsScenarioRunner(),
            legacyRunner);
        var reportPath = LegacyDifferentialComparison.WriteReport(report);

        Assert.True(
            report.IsMatch,
            BuildFailureMessage(report, reportPath));
    }

    private static string BuildFailureMessage(LegacyDifferentialReport report, string reportPath)
    {
        static string Describe(IReadOnlyList<string> differences)
            => differences.Count == 0 ? "<none>" : string.Join("; ", differences);

        return string.Join(
            Environment.NewLine,
            $"Differential mismatch for scenario '{report.ScenarioId}'.",
            $"iKeyd vs expected: {Describe(report.IKeydVsExpected)}",
            $"legacy vs expected: {Describe(report.LegacyVsExpected)}",
            $"iKeyd vs legacy: {Describe(report.IKeydVsLegacy)}",
            $"report: {reportPath}");
    }
}
