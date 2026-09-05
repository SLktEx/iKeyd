using Xunit;

namespace iKeyd.Compatibility.Tests;

public sealed class IKeydCoreScenarioRunnerTests
{
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Theory]
    [InlineData("s-chord-k-q-at-39ms")]
    [InlineData("s-chord-k-q-at-40ms")]
    [InlineData("s-chord-k-q-at-41ms")]
    public async Task Legacy_chord_boundary_scenarios_match_iKeyd_core(string scenarioId)
    {
        var scenario = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Single(item => item.Id == scenarioId);
        var runner = new IKeydCoreScenarioRunner();

        var result = await runner.RunAsync(scenario);
        var differences = CompatibilityScenarioDiff.Compare(scenario, result);

        Assert.Empty(differences);
        Assert.Equal("iKeyd.Core", result.Runner);
        Assert.Equal("core-chord-engine", result.Metadata["scope"]);
    }

    [Fact]
    public async Task Core_runner_rejects_modifier_scenarios_until_modifier_routing_is_supported()
    {
        var scenario = new CompatibilityScenario
        {
            Id = "modifier-not-supported",
            InitialState = new ScenarioInitialState
            {
                Mode = "S",
                Ime = "on",
                Modifiers = ["Shift"]
            },
            Input =
            [
                new ScenarioInputEvent { AtMs = 0, Kind = "keyDown", Key = "Q" }
            ],
            Expected = new ScenarioExpected()
        };

        var runner = new IKeydCoreScenarioRunner();

        await Assert.ThrowsAsync<NotSupportedException>(() => runner.RunAsync(scenario));
    }
}
