using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class IKeydRuntimeScenarioRunnerTests
{
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Fact]
    public async Task Runtime_long_tail_scenarios_match_recorded_runtime_actions()
    {
        var scenarios = iKeyd.Compatibility.Tests.CompatibilityScenarioCatalog
            .LoadDirectory(ScenarioDirectory)
            .Where(scenario => scenario.Tags.Any(tag =>
                string.Equals(tag, "runtime-long-tail", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(scenario => scenario.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(scenarios);
        var runner = new IKeydRuntimeScenarioRunner();
        foreach (var scenario in scenarios)
        {
            var result = await runner.RunAsync(scenario);
            var differences = iKeyd.Compatibility.Tests.CompatibilityScenarioDiff.Compare(scenario, result);
            Assert.True(
                differences.Count == 0,
                $"{scenario.Id}: {string.Join("; ", differences)}");
            Assert.NotEmpty(scenario.InventoryIds);
            Assert.Contains(scenario.OracleTargets, target =>
                string.Equals(target, "ikeyd-runtime", StringComparison.OrdinalIgnoreCase));
        }
    }
}
