using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class OneShotModifierBehaviorTests
{
    [Fact]
    public void Clean_tap_arms_one_shot_after_releasing_held_modifier()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.OSM("Control"));

        var down = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 5);
        var up = runtime.OnKeyUp(source, 10);

        Assert.True(down.Suppress);
        Assert.Equal([BehaviorAction.ModifierDown("Control")], down.Actions);
        Assert.True(repeat.Suppress);
        Assert.Empty(repeat.Actions);
        Assert.True(up.Suppress);
        Assert.Equal(
            [BehaviorAction.ModifierUp("Control"), BehaviorAction.ModifierOneShot("Control")],
            up.Actions);
    }

    [Fact]
    public void Interrupted_hold_releases_modifier_without_arming_one_shot()
    {
        var source = new KeyId("A");
        var other = new KeyId("B");
        var runtime = Runtime(source, StandardBehaviors.OSM("Shift"));

        runtime.OnKeyDown(source, 0);
        var interrupt = runtime.ObserveKeyDown(other, 10);
        var up = runtime.OnKeyUp(source, 20);

        Assert.Empty(interrupt.Actions);
        Assert.Equal([BehaviorAction.ModifierUp("Shift")], up.Actions);
    }

    [Fact]
    public void Cancellation_releases_held_modifier_without_arming_one_shot()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.OSM("Alt"));

        runtime.OnKeyDown(source, 0);
        var cancelled = runtime.CancelAll();

        Assert.Equal([BehaviorAction.ModifierUp("Alt")], cancelled);
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Control")]
    [InlineData("Shift")]
    [InlineData("Alt")]
    [InlineData("Gui")]
    [InlineData("Win")]
    [InlineData("Super")]
    public void Factory_builds_OSM_with_supported_modifier_aliases(string modifier)
    {
        var definition = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile("OSM", [modifier]));

        Assert.NotNull(definition);
    }

    [Fact]
    public void Factory_rejects_invalid_OSM_shape_or_modifier()
    {
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("OSM", [])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("OSM", ["Ctrl", "Shift"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("OSM", ["Hyper"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(
                "OSM",
                ["Ctrl"],
                new Dictionary<string, string> { ["unexpected"] = "true" })));
    }

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
