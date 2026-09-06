using iKeyd.Compatibility.Tests;
using iKeyd.Core.Desktop;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class Issue57RemainingCompatibilityTests
{
    [Fact]
    public async Task SM_C_vertical_movement_uses_quarter_primary_height()
    {
        var scenario = Scenario(
            "issue57-sm-quarter-height-up",
            [Down(10, "C"), Down(11, "I"), Up(12, "I"), Up(13, "C")],
            [Action("mouse", "move-by:0,-270")]);

        var result = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);
        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, result));
    }

    [Fact]
    public async Task SM_H_is_noop_when_right_button_is_up()
    {
        var scenario = Scenario(
            "issue57-sm-right-up-noop",
            [Down(10, "H"), Up(11, "H")],
            []);

        var result = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);
        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, result));
    }

    [Fact]
    public async Task SM_H_releases_an_already_held_right_button()
    {
        var scenario = Scenario(
            "issue57-sm-right-release-only",
            [Down(10, "H"), Up(11, "H")],
            [Action("mouse", "button:right:up")]);

        var result = await new IKeydRuntimeScenarioRunner(DesktopMouseButton.Right).RunAsync(scenario);
        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, result));
    }

    private static CompatibilityScenario Scenario(
        string id,
        List<ScenarioInputEvent> input,
        List<ObservedAction> actions)
        => new()
        {
            Id = id,
            InitialState = new ScenarioInitialState { Mode = "S", Ime = "off", Layers = ["S", "M"] },
            Input = input,
            Expected = new ScenarioExpected { Actions = actions },
            Tags = ["issue57-deterministic"]
        };

    private static ScenarioInputEvent Down(long atMs, string key)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(long atMs, string key)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };

    private static ObservedAction Action(string kind, string value)
        => new() { Kind = kind, Value = value };
}
