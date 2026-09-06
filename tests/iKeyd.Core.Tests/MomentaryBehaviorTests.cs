using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class MomentaryBehaviorTests
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
        Assert.True(up.Suppress);
        Assert.Equal([BehaviorAction.LayerOff("NAV")], up.Actions);
    }

    [Fact]
    public void MO_cancel_releases_owned_layer_once()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.MO("NAV"));

        runtime.OnKeyDown(source, 0);
        var first = runtime.CancelAll();
        var second = runtime.CancelAll();

        Assert.Equal([BehaviorAction.LayerOff("NAV")], first);
        Assert.Empty(second);
    }

    [Fact]
    public void MOD_owns_modifier_for_physical_hold_and_cancel_cleans_it_up()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.MOD("Control"));

        var down = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 5);
        var cancel = runtime.CancelAll();

        Assert.Equal([BehaviorAction.ModifierDown("Control")], down.Actions);
        Assert.True(repeat.Suppress);
        Assert.Empty(repeat.Actions);
        Assert.Equal([BehaviorAction.ModifierUp("Control")], cancel);
    }

    [Theory]
    [InlineData("MO", "NAV")]
    [InlineData("MOD", "Ctrl")]
    [InlineData("MOD", "Control")]
    [InlineData("MOD", "Gui")]
    [InlineData("MOD", "Win")]
    [InlineData("MOD", "Super")]
    public void Factory_builds_momentary_helpers(string name, string argument)
    {
        var definition = BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(name, [argument]));
        Assert.NotNull(definition);
    }

    [Fact]
    public void Factory_rejects_invalid_momentary_arguments_and_options()
    {
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("MO", [])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("MOD", ["Ctrl", "Shift"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("MOD", ["Hyper"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(
                "MO",
                ["NAV"],
                new Dictionary<string, string> { ["unexpected"] = "true" })));
    }

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
