using iKeyd.Compatibility.Tests;
using iKeyd.Core.Desktop;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class Issue57RuntimeCompatibilityTests
{
    [Fact]
    public async Task Remaining_SM_mouse_and_window_group_branches_match_legacy_semantics()
    {
        var runner = new IKeydRuntimeScenarioRunner();
        var scenarios = new[]
        {
            Scenario(
                "issue57-sm-left-toggle",
                ["S", "M"],
                [Down(10, "Y"), Up(11, "Y"), Down(12, "Y"), Up(13, "Y")],
                [Action("mouse", "button:left:down"), Action("mouse", "button:left:up")]),
            Scenario(
                "issue57-sm-top-left-corner",
                ["S", "M"],
                [Down(10, "N"), Up(11, "N")],
                [Action("mouse", "move:101,101")]),
            Scenario(
                "issue57-sm-bottom-right-corner",
                ["S", "M"],
                [Down(10, "M"), Up(11, "M")],
                [Action("mouse", "move:899,699")]),
            Scenario(
                "issue57-sm-quarter-height-up",
                ["S", "M"],
                [Down(10, "C"), Down(11, "I"), Up(12, "I"), Up(13, "C")],
                [Action("mouse", "move-by:0,-270")]),
            Scenario(
                "issue57-window-group-toggle-next",
                ["M", "S"],
                [Down(10, "G"), Up(11, "G"), Up(12, "SPACE"), Down(13, "G"), Up(14, "G")],
                [Action("window", "activate")]),
            Scenario(
                "issue57-window-group-reset",
                ["M", "S"],
                [Down(10, "G"), Up(11, "G"), Down(12, "B"), Up(13, "B"), Up(14, "SPACE"), Down(15, "G"), Up(16, "G")],
                []),
            Scenario(
                "issue57-bottom-window-of-class",
                ["M"],
                [Down(10, "B"), Up(11, "B")],
                [Action("window", "activate")])
        };

        foreach (var scenario in scenarios)
        {
            var result = await runner.RunAsync(scenario);
            var differences = CompatibilityScenarioDiff.Compare(scenario, result);
            Assert.True(differences.Count == 0, $"{scenario.Id}: {string.Join("; ", differences)}");
        }
    }

    [Fact]
    public async Task SM_H_preserves_legacy_right_button_release_only_typo()
    {
        var scenario = Scenario(
            "issue57-sm-right-release-only",
            ["S", "M"],
            [Down(10, "H"), Up(11, "H")],
            [Action("mouse", "button:right:up")]);

        var result = await new IKeydRuntimeScenarioRunner(DesktopMouseButton.Right).RunAsync(scenario);
        var differences = CompatibilityScenarioDiff.Compare(scenario, result);
        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    private static CompatibilityScenario Scenario(
        string id,
        List<string> layers,
        List<ScenarioInputEvent> input,
        List<ObservedAction> actions)
        => new()
        {
            Id = id,
            InitialState = new ScenarioInitialState { Mode = "S", Ime = "off", Layers = layers },
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
