using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class IKeydRuntimeScenarioRunnerTests
{
    private const string RuntimeLongTailTag = "runtime-long-tail";
    private const string SupersededPointerMotionTag = "superseded-pointer-motion";
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Fact]
    public async Task Runtime_long_tail_scenarios_match_recorded_runtime_actions()
    {
        var scenarios = iKeyd.Compatibility.Tests.CompatibilityScenarioCatalog
            .LoadDirectory(ScenarioDirectory)
            .Where(scenario => HasTag(scenario, RuntimeLongTailTag))
            .Where(scenario => !HasTag(scenario, SupersededPointerMotionTag))
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

    [Fact]
    public void Superseded_stepped_mouse_scenarios_remain_as_legacy_oracles()
    {
        var scenarios = iKeyd.Compatibility.Tests.CompatibilityScenarioCatalog
            .LoadDirectory(ScenarioDirectory)
            .Where(scenario => HasTag(scenario, SupersededPointerMotionTag))
            .OrderBy(scenario => scenario.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(4, scenarios.Length);
        Assert.All(scenarios, scenario =>
        {
            Assert.True(HasTag(scenario, "legacy"));
            Assert.True(HasTag(scenario, "mouse"));
            Assert.NotEmpty(scenario.InventoryIds);
        });
    }

    private static bool HasTag(iKeyd.Compatibility.Tests.CompatibilityScenario scenario, string tag)
        => scenario.Tags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
}
