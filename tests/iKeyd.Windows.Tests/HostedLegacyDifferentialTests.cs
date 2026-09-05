using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class HostedLegacyDifferentialTests
{
    private const string HostedLegacyTag = "hosted-legacy";
    private const string RuntimeTag = "hosted-legacy-runtime";
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Fact]
    [Trait("Category", "HostedLegacyDifferentialE2E")]
    public async Task Tagged_scenarios_match_the_real_legacy_exe_on_hosted_windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var baseLegacyRunner = new HostedTModeLegacyRunner();
        if (!baseLegacyRunner.IsAvailable)
            return;

        var scenarios = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Where(item => HasTag(item, HostedLegacyTag))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(scenarios);

        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            try
            {
                var runtimeScenario = HasTag(scenario, RuntimeTag);
                ICompatibilityScenarioRunner iKeydRunner = runtimeScenario
                    ? new HotkeySkgRuntimeScenarioRunner()
                    : new WindowsScenarioRunner();
                ICompatibilityScenarioRunner legacyRunner = runtimeScenario
                    ? new RuntimeEventCaptureRunner(
                        new RuntimeInputInjectionRunner(new HostedTModeLegacyRunner()))
                    : new HostedTModeLegacyRunner();

                var report = await LegacyDifferentialComparison.RunAsync(
                    scenario,
                    iKeydRunner,
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
        Assert.Equal(500, adapted.Input[0].AtMs);
        Assert.Equal(510, adapted.Input[1].AtMs);
        Assert.Equal("K", scenario.InitialState.Mode);
        Assert.Equal("on", scenario.InitialState.Ime);
        Assert.Equal(0, scenario.Input[0].AtMs);
    }

    [Fact]
    public void Hosted_adapter_uses_M3_for_S_and_M4_then_M3_for_K()
    {
        Assert.Equal(
            new byte[] { 0x33 },
            HostedTModeLegacyRunner.ResolveModeSelectionDigits("S"));
        Assert.Equal(
            new byte[] { 0x34, 0x33 },
            HostedTModeLegacyRunner.ResolveModeSelectionDigits("K"));
    }

    [Theory]
    [InlineData(@"C:\runner\hotkeySKG.exe", "hotkeySKG")]
    [InlineData(@"C:\runner\compiled-hotkeySKG.exe", "compiled-hotkeySKG")]
    [InlineData(null, "hotkeySKG")]
    public void Hosted_adapter_tracks_the_configured_legacy_process_name(
        string? executablePath,
        string expected)
    {
        Assert.Equal(expected, HostedTModeLegacyRunner.ResolveLegacyProcessName(executablePath));
    }

    private static bool HasTag(CompatibilityScenario scenario, string tag)
        => scenario.Tags.Any(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase));

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
