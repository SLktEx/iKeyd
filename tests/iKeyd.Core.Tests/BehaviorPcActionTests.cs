using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class BehaviorPcActionTests
{
    [Fact]
    public void MO_activates_immediately_and_releases_on_key_up()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.MO("NAV"));

        var down = runtime.OnKeyDown(source, 0);
        var up = runtime.OnKeyUp(source, 10);

        Assert.True(down.Suppress);
        Assert.Equal([BehaviorAction.LayerOn("NAV")], down.Actions);
        Assert.Equal([BehaviorAction.LayerOff("NAV")], up.Actions);
    }

    [Fact]
    public void MOD_owns_modifier_and_cancel_releases_it()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.MOD("Control"));

        var down = runtime.OnKeyDown(source, 0);
        var cancel = runtime.CancelAll();

        Assert.Equal([BehaviorAction.ModifierDown("Control")], down.Actions);
        Assert.Equal([BehaviorAction.ModifierUp("Control")], cancel);
    }

    [Fact]
    public void Press_action_fires_once_even_when_physical_down_repeats()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.Press(BehaviorAction.Media("PlayPause")));

        var first = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 20);
        var up = runtime.OnKeyUp(source, 30);

        Assert.Equal([BehaviorAction.Media("PlayPause")], first.Actions);
        Assert.Empty(repeat.Actions);
        Assert.True(repeat.Suppress);
        Assert.True(up.Suppress);
        Assert.Empty(up.Actions);
    }

    [Theory]
    [InlineData("MO", "NAV")]
    [InlineData("MOD", "Ctrl")]
    [InlineData("MEDIA", "PlayPause")]
    [InlineData("WINDOW", "LeftHalf")]
    [InlineData("CLIPBOARD", "History")]
    public void Factory_builds_standard_PC_helpers(string name, string argument)
    {
        var invocation = new BehaviorInvocationProfile(name, [argument]);

        var definition = BehaviorDefinitionFactory.Create(invocation);

        Assert.NotNull(definition);
    }

    [Fact]
    public void Factory_supports_option_backed_mouse_text_and_macro_payloads()
    {
        var mouse = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile(
                "MOUSE_MOVE",
                [],
                new Dictionary<string, string> { ["x"] = "-30", ["y"] = "10" }));
        var text = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile(
                "TEXT",
                [],
                new Dictionary<string, string> { ["value"] = "^+{}" }));
        var macro = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile(
                "MACRO",
                [],
                new Dictionary<string, string> { ["template"] = "hello, world" }));

        Assert.Equal(
            [BehaviorAction.MouseMove(-30, 10)],
            Runtime(new KeyId("A"), mouse).OnKeyDown(new KeyId("A"), 0).Actions);
        Assert.Equal(
            [BehaviorAction.SendText("^+{}")],
            Runtime(new KeyId("B"), text).OnKeyDown(new KeyId("B"), 0).Actions);
        Assert.Equal(
            [BehaviorAction.Macro("hello, world")],
            Runtime(new KeyId("C"), macro).OnKeyDown(new KeyId("C"), 0).Actions);
    }

    [Fact]
    public void Direct_IR_payloads_remain_supported_for_programmatic_profiles()
    {
        var mouse = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile("MOUSE_MOVE", ["-30", "10"]));
        var macro = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile("MACRO", ["hello, world"]));

        Assert.Equal(
            [BehaviorAction.MouseMove(-30, 10)],
            Runtime(new KeyId("A"), mouse).OnKeyDown(new KeyId("A"), 0).Actions);
        Assert.Equal(
            [BehaviorAction.Macro("hello, world")],
            Runtime(new KeyId("B"), macro).OnKeyDown(new KeyId("B"), 0).Actions);
    }

    [Fact]
    public void Unsupported_PC_helper_options_are_rejected()
    {
        var invocation = new BehaviorInvocationProfile(
            "MEDIA",
            ["PlayPause"],
            new Dictionary<string, string> { ["unexpected"] = "true" });

        Assert.Throws<InvalidDataException>(() => BehaviorDefinitionFactory.Create(invocation));
    }

    [Fact]
    public void Missing_required_payload_option_is_rejected()
    {
        var invocation = new BehaviorInvocationProfile(
            "MOUSE_MOVE",
            [],
            new Dictionary<string, string> { ["x"] = "-30" });

        Assert.Throws<InvalidDataException>(() => BehaviorDefinitionFactory.Create(invocation));
    }

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
