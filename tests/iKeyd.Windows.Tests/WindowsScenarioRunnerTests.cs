using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsScenarioRunnerTests
{
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Theory]
    [Trait("Category", "WindowsE2E")]
    [InlineData("s-chord-k-q-immediate")]
    [InlineData("s-k-q-after-100ms")]
    public async Task Realtime_safe_scenarios_match_through_hook_core_and_SendInput(string scenarioId)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var scenario = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Single(item => item.Id == scenarioId);
        var runner = new WindowsScenarioRunner();

        var result = await runner.RunAsync(scenario);
        var differences = CompatibilityScenarioDiff.Compare(scenario, result);

        Assert.Empty(differences);
        Assert.Equal("iKeyd.Windows", result.Runner);
        Assert.Equal("windows-hook-core-sendinput", result.Metadata["scope"]);
    }
}
