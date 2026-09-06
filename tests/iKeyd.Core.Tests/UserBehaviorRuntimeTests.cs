using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class UserBehaviorRuntimeTests
{
    [Fact]
    public void Custom_behavior_can_change_release_logic_from_local_bool_state()
    {
        var definition = CreateSmartLayerTapDefinition();
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S", [], [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("SMART_LT", ["X", "NUM"]))]),
                new AutomationKeymapProfile("NUM", [], [])
            ],
            behaviorDefinitions: [definition]);

        var runtime = new BehaviorRuntime(
            profile.GetKeymap("S").BuildBehaviorBindings(profile.BehaviorDefinitions));

        runtime.OnKeyDown("A", 0);
        var tap = runtime.OnKeyUp("A", 50);
        Assert.Equal([BehaviorAction.SendKey("X")], tap.Actions);

        runtime.OnKeyDown("A", 100);
        var interrupt = runtime.OnKeyDown("B", 120);
        var release = runtime.OnKeyUp("A", 130);

        Assert.Equal([BehaviorAction.LayerOn("NUM")], interrupt.Actions);
        Assert.Equal([BehaviorAction.LayerOff("NUM")], release.Actions);
    }

    [Fact]
    public void Custom_behavior_owned_modifier_is_released_on_cancel()
    {
        var definition = new UserBehaviorDefinitionProfile(
            "HOLD_CTRL",
            [],
            handlers:
            [
                new UserBehaviorHandlerProfile(
                    "press",
                    [],
                    [new UserBehaviorStatementProfile("modifier_down", value: "Ctrl")])
            ]);

        var profile = new AutomationProfile(
            40,
            [new AutomationKeymapProfile(
                "S", [], [],
                [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("HOLD_CTRL", []))])],
            behaviorDefinitions: [definition]);
        var runtime = new BehaviorRuntime(
            profile.GetKeymap("S").BuildBehaviorBindings(profile.BehaviorDefinitions));

        var down = runtime.OnKeyDown("A", 0);
        var cancelled = runtime.CancelAll();

        Assert.Equal([BehaviorAction.ModifierDown("Ctrl")], down.Actions);
        Assert.Equal([BehaviorAction.ModifierUp("Ctrl")], cancelled);
    }

    [Fact]
    public void User_behavior_definitions_round_trip_through_profile_json()
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S", [], [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("SMART_LT", ["X", "NUM"]))]),
                new AutomationKeymapProfile("NUM", [], [])
            ],
            behaviorDefinitions: [CreateSmartLayerTapDefinition()]);

        var parsed = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(profile));
        var definition = Assert.Single(parsed.BehaviorDefinitions.Values);

        Assert.Equal("SMART_LT", definition.Name);
        Assert.Equal(["tap_key", "layer_name"], definition.Parameters);
        Assert.False(Assert.Single(definition.Locals).InitialValue);
        Assert.Equal(2, definition.Handlers.Count);

        var runtime = new BehaviorRuntime(
            parsed.GetKeymap("S").BuildBehaviorBindings(parsed.BehaviorDefinitions));
        runtime.OnKeyDown("A", 0);
        Assert.Equal([BehaviorAction.SendKey("X")], runtime.OnKeyUp("A", 10).Actions);
    }

    [Fact]
    public void User_behavior_argument_count_is_validated()
    {
        var definition = new UserBehaviorDefinitionProfile("CUSTOM", ["key"]);
        var definitions = new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [definition.Name] = definition
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("CUSTOM", []), definitions));

        Assert.Contains("requires 1 arguments", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standard_behavior_names_cannot_be_shadowed()
    {
        var error = Assert.Throws<ArgumentException>(() => new AutomationProfile(
            40,
            [new AutomationKeymapProfile("S", [], [])],
            behaviorDefinitions: [new UserBehaviorDefinitionProfile("LT", [])]));

        Assert.Contains("standard behavior", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static UserBehaviorDefinitionProfile CreateSmartLayerTapDefinition()
        => new(
            "SMART_LT",
            ["tap_key", "layer_name"],
            [new UserBehaviorLocalProfile("interrupted")],
            [
                new UserBehaviorHandlerProfile(
                    "interrupt",
                    ["other"],
                    [
                        new UserBehaviorStatementProfile("set_bool", target: "interrupted", value: "true"),
                        new UserBehaviorStatementProfile("layer_on", value: "layer_name")
                    ]),
                new UserBehaviorHandlerProfile(
                    "release",
                    [],
                    [
                        new UserBehaviorStatementProfile(
                            "if_bool",
                            condition: "interrupted",
                            thenStatements: [new UserBehaviorStatementProfile("layer_off", value: "layer_name")],
                            elseStatements: [new UserBehaviorStatementProfile("send", value: "tap_key")])
                    ])
            ]);
}
