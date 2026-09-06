using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class OneShotLayerBehaviorTests
{
    [Fact]
    public void Clean_tap_arms_one_shot_after_releasing_momentary_layer()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.OSL("NUM"));

        var down = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 5);
        var up = runtime.OnKeyUp(source, 10);

        Assert.True(down.Suppress);
        Assert.Equal([BehaviorAction.LayerOn("NUM")], down.Actions);
        Assert.True(repeat.Suppress);
        Assert.Empty(repeat.Actions);
        Assert.True(up.Suppress);
        Assert.Equal(
            [BehaviorAction.LayerOff("NUM"), BehaviorAction.LayerOneShot("NUM")],
            up.Actions);
    }

    [Fact]
    public void Interrupted_hold_releases_layer_without_arming_one_shot()
    {
        var source = new KeyId("A");
        var other = new KeyId("B");
        var runtime = Runtime(source, StandardBehaviors.OSL("NUM"));

        runtime.OnKeyDown(source, 0);
        var interrupt = runtime.ObserveKeyDown(other, 10);
        var up = runtime.OnKeyUp(source, 20);

        Assert.Empty(interrupt.Actions);
        Assert.Equal([BehaviorAction.LayerOff("NUM")], up.Actions);
    }

    [Fact]
    public void Cancellation_releases_held_layer_without_arming_one_shot()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.OSL("NUM"));

        runtime.OnKeyDown(source, 0);
        var cancelled = runtime.CancelAll();

        Assert.Equal([BehaviorAction.LayerOff("NUM")], cancelled);
    }

    [Fact]
    public void Factory_builds_OSL()
    {
        var definition = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile("OSL", ["NUM"]));

        Assert.NotNull(definition);
    }

    [Fact]
    public void Factory_rejects_invalid_OSL_shape()
    {
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("OSL", [])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("OSL", ["NUM", "NAV"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(
                "OSL",
                ["NUM"],
                new Dictionary<string, string> { ["unexpected"] = "true" })));
    }

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
