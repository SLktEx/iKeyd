using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class HostedAutoHotkeySourceDifferentialTests
{
    private const string HostedLegacyTag = "hosted-legacy";
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Fact]
    [Trait("Category", "HostedAhkSourceDifferentialE2E")]
    public async Task IKeyd_and_original_AHK_source_match_on_hosted_windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sourceRunner = new HostedAutoHotkeySourceRunner();
        if (!sourceRunner.IsAvailable)
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
                    sourceRunner);
                var reportPath = LegacyDifferentialComparison.WriteReport(report);

                if (!report.IsMatch)
                    failures.Add(BuildFailureMessage(report, reportPath));
            }
            catch (Exception error)
            {
                failures.Add(
                    $"AHK source differential runner failed for scenario '{scenario.Id}': " +
                    $"{error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            failures.Count == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    [Fact]
    public void Source_oracle_identity_is_pinned_separately_from_compiled_EXE()
    {
        Assert.Equal(
            "fde46d179a2cfb8123a314d4ea6b8de714a65302867d4b3a654af07f9472bab7",
            HostedAutoHotkeySourceRunner.ReferenceSourceSha256);
        Assert.NotEqual(
            LegacyExecutableScenarioRunner.ReferenceSha256,
            HostedAutoHotkeySourceRunner.ReferenceSourceSha256);
        Assert.Contains("hotkeySKG.ahk", new HostedAutoHotkeySourceRunner().Name, StringComparison.Ordinal);
    }

    private static string BuildFailureMessage(LegacyDifferentialReport report, string reportPath)
    {
        static string Describe(IReadOnlyList<string> differences)
            => differences.Count == 0 ? "<none>" : string.Join("; ", differences);

        return string.Join(
            Environment.NewLine,
            $"AHK source differential mismatch for scenario '{report.ScenarioId}'.",
            $"iKeyd vs expected: {Describe(report.IKeydVsExpected)}",
            $"AHK source vs expected: {Describe(report.LegacyVsExpected)}",
            $"iKeyd vs AHK source: {Describe(report.IKeydVsLegacy)}",
            $"report: {reportPath}");
    }
}
