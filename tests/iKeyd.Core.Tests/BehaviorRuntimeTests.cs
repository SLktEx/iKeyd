using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class BehaviorRuntimeTests
{
    [Fact]
    public void LT_tap_emits_tap_key_when_released_before_tapping_term()
    {
        var source = new KeyId("A");
        var tap = new KeyId("Z");
        var runtime = CreateRuntime(source, StandardBehaviors.LT("NUM", tap));

        var down = runtime.OnKeyDown(source, 0);
        var up = runtime.OnKeyUp(source, 100);

        Assert.True(down.Suppress);
        Assert.Empty(down.Actions);
        Assert.True(up.Suppress);
        Assert.Equal([BehaviorAction.SendKey(tap)], up.Actions);
        Assert.Equal(0, runtime.ActiveCount);
    }

    [Fact]
    public void LT_timeout_activates_layer_and_release_deactivates_it()
    {
        var source = new KeyId("A");
        var runtime = CreateRuntime(source, StandardBehaviors.LT("NUM", new KeyId("Z")));

        runtime.OnKeyDown(source, 0);
        var timeout = runtime.AdvanceTo(LayerTapOptions.DefaultTappingTermMs);
        var up = runtime.OnKeyUp(source, LayerTapOptions.DefaultTappingTermMs + 1);

        Assert.Equal([BehaviorAction.LayerOn("NUM")], timeout);
        Assert.Equal([BehaviorAction.LayerOff("NUM")], up.Actions);
    }

    [Fact]
    public void LT_other_key_press_can_resolve_hold_before_timeout_without_suppressing_other_key()
    {
        var source = new KeyId("A");
        var other = new KeyId("B");
        var runtime = CreateRuntime(source, StandardBehaviors.LT("NUM", new KeyId("Z")));

        runtime.OnKeyDown(source, 0);
        var interrupt = runtime.OnKeyDown(other, 50);
        var up = runtime.OnKeyUp(source, 60);

        Assert.False(interrupt.Suppress);
        Assert.Equal([BehaviorAction.LayerOn("NUM")], interrupt.Actions);
        Assert.Equal([BehaviorAction.LayerOff("NUM")], up.Actions);
    }

    [Fact]
    public void LT_can_ignore_other_key_press_when_configured()
    {
        var source = new KeyId("A");
        var tap = new KeyId("Z");
        var runtime = CreateRuntime(
            source,
            StandardBehaviors.LT(
                "NUM",
                tap,
                new LayerTapOptions
                {
                    TappingTermMs = 200,
                    HoldOnOtherKeyPress = false
                }));

        runtime.OnKeyDown(source, 0);
        var interrupt = runtime.OnKeyDown(new KeyId("B"), 50);
        var up = runtime.OnKeyUp(source, 100);

        Assert.False(interrupt.Suppress);
        Assert.Empty(interrupt.Actions);
        Assert.Equal([BehaviorAction.SendKey(tap)], up.Actions);
    }

    [Fact]
    public void LT_instances_can_use_different_tapping_terms()
    {
        var fastSource = new KeyId("A");
        var slowSource = new KeyId("B");
        var runtime = new BehaviorRuntime(new Dictionary<KeyId, BehaviorDefinition>
        {
            [fastSource] = StandardBehaviors.LT(
                "FAST",
                new KeyId("X"),
                new LayerTapOptions { TappingTermMs = 100, HoldOnOtherKeyPress = false }),
            [slowSource] = StandardBehaviors.LT(
                "SLOW",
                new KeyId("Y"),
                new LayerTapOptions { TappingTermMs = 250, HoldOnOtherKeyPress = false })
        });

        runtime.OnKeyDown(fastSource, 0);
        runtime.OnKeyDown(slowSource, 1);
        var actions = runtime.AdvanceTo(101);

        Assert.Equal([BehaviorAction.LayerOn("FAST")], actions);

        var slowUp = runtime.OnKeyUp(slowSource, 150);
        Assert.Equal([BehaviorAction.SendKey(new KeyId("Y"))], slowUp.Actions);

        var fastUp = runtime.OnKeyUp(fastSource, 151);
        Assert.Equal([BehaviorAction.LayerOff("FAST")], fastUp.Actions);
    }

    [Fact]
    public void Repeated_key_down_does_not_restart_LT_timer()
    {
        var source = new KeyId("A");
        var runtime = CreateRuntime(source, StandardBehaviors.LT("NUM", new KeyId("Z")));

        runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 150);
        var timeout = runtime.AdvanceTo(200);

        Assert.True(repeat.Suppress);
        Assert.Empty(repeat.Actions);
        Assert.Equal([BehaviorAction.LayerOn("NUM")], timeout);
    }

    [Fact]
    public void Cancel_releases_owned_hold_resource_but_does_not_emit_pending_tap()
    {
        var source = new KeyId("A");
        var runtime = CreateRuntime(source, StandardBehaviors.LT("NUM", new KeyId("Z")));

        runtime.OnKeyDown(source, 0);
        Assert.Empty(runtime.CancelAll());

        runtime.OnKeyDown(source, 300);
        Assert.Equal([BehaviorAction.LayerOn("NUM")], runtime.AdvanceTo(500));
        Assert.Equal([BehaviorAction.LayerOff("NUM")], runtime.CancelAll());
        Assert.Equal(0, runtime.ActiveCount);
    }

    [Fact]
    public void Runtime_rejects_non_monotonic_timestamps()
    {
        var source = new KeyId("A");
        var runtime = CreateRuntime(source, StandardBehaviors.LT("NUM", new KeyId("Z")));

        runtime.OnKeyDown(source, 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.AdvanceTo(99));
    }

    private static BehaviorRuntime CreateRuntime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition>
        {
            [source] = definition
        });
}
