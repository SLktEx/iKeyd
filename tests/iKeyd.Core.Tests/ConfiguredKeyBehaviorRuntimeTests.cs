using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Runtime;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ConfiguredKeyBehaviorRuntimeTests
{
    [Fact]
    public void Quick_release_emits_tap()
    {
        var runtime = Runtime(Behavior(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("NAV")));

        Assert.True(runtime.OnKeyDown(KeyCode.Space, 100).Consumed);
        var released = runtime.OnKeyUp(KeyCode.Space, 200);

        Assert.True(released.Consumed);
        Assert.Equal(1, released.Transitions.Count);
        Assert.Equal(KeyBehaviorTransitionKind.Tap, released.Transitions[0].Kind);
        Assert.Equal(KeyBehaviorAction.Key("Space"), released.Transitions[0].Action);
        Assert.Equal(0, runtime.ActiveHoldCount);
    }

    [Fact]
    public void Release_after_timeout_is_hold_without_tap()
    {
        var runtime = Runtime(Behavior(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("NAV")));

        runtime.OnKeyDown(KeyCode.Space, 100);
        var released = runtime.OnKeyUp(KeyCode.Space, 300);

        Assert.Equal(2, released.Transitions.Count);
        Assert.Equal(KeyBehaviorTransitionKind.HoldStarted, released.Transitions[0].Kind);
        Assert.Equal(KeyBehaviorTransitionKind.HoldEnded, released.Transitions[1].Kind);
        Assert.DoesNotContain(
            Enumerable.Range(0, released.Transitions.Count).Select(index => released.Transitions[index].Kind),
            kind => kind == KeyBehaviorTransitionKind.Tap);
    }

    [Fact]
    public void Hold_preferred_interrupt_activates_hold_before_next_key()
    {
        var runtime = Runtime(Behavior(KeyCode.A, KeyBehaviorAction.Key("A"), KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control)));

        runtime.OnKeyDown(KeyCode.A, 0);
        var qDown = runtime.OnKeyDown(KeyCode.Q, 50);

        Assert.False(qDown.Consumed);
        Assert.Equal(1, qDown.Transitions.Count);
        Assert.Equal(KeyBehaviorTransitionKind.HoldStarted, qDown.Transitions[0].Kind);
        Assert.Equal(KeyBehaviorActionKind.Modifier, runtime.GetActiveHoldAt(0).Action.Kind);
    }

    [Fact]
    public void Tap_preferred_interrupt_emits_tap_before_next_key()
    {
        var runtime = Runtime(new KeyBehaviorBinding(
            KeyCode.A,
            KeyBehaviorAction.Key("A"),
            KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control),
            timeoutMs: 180,
            interrupt: TapHoldInterruptPolicy.Tap));

        runtime.OnKeyDown(KeyCode.A, 0);
        var qDown = runtime.OnKeyDown(KeyCode.Q, 50);

        Assert.False(qDown.Consumed);
        Assert.Equal(1, qDown.Transitions.Count);
        Assert.Equal(KeyBehaviorTransitionKind.Tap, qDown.Transitions[0].Kind);
        Assert.Equal(0, runtime.ActiveHoldCount);
    }

    [Fact]
    public void Hold_only_behavior_activates_and_releases_immediately()
    {
        var runtime = Runtime(new KeyBehaviorBinding(
            KeyCode.Muhenkan,
            tap: null,
            KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control)));

        var down = runtime.OnKeyDown(KeyCode.Muhenkan, 0);
        Assert.True(down.Consumed);
        Assert.Equal(KeyBehaviorTransitionKind.HoldStarted, down.Transitions[0].Kind);
        Assert.Equal(1, runtime.ActiveHoldCount);

        var up = runtime.OnKeyUp(KeyCode.Muhenkan, 10);
        Assert.True(up.Consumed);
        Assert.Equal(KeyBehaviorTransitionKind.HoldEnded, up.Transitions[0].Kind);
        Assert.Equal(0, runtime.ActiveHoldCount);
    }

    [Fact]
    public void Home_row_roll_can_keep_multiple_resolved_holds()
    {
        var runtime = Runtime(
            Behavior(KeyCode.A, KeyBehaviorAction.Key("A"), KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control)),
            Behavior(KeyCode.S, KeyBehaviorAction.Key("S"), KeyBehaviorAction.Modifier(KeyBehaviorModifier.Shift)));

        runtime.OnKeyDown(KeyCode.A, 0);
        var sDown = runtime.OnKeyDown(KeyCode.S, 30);
        Assert.True(sDown.Consumed);
        Assert.Equal(KeyBehaviorTransitionKind.HoldStarted, sDown.Transitions[0].Kind);

        var qDown = runtime.OnKeyDown(KeyCode.Q, 60);
        Assert.False(qDown.Consumed);
        Assert.Equal(KeyBehaviorTransitionKind.HoldStarted, qDown.Transitions[0].Kind);
        Assert.Equal(2, runtime.ActiveHoldCount);

        runtime.OnKeyUp(KeyCode.S, 80);
        runtime.OnKeyUp(KeyCode.A, 90);
        Assert.Equal(0, runtime.ActiveHoldCount);
    }

    private static ConfiguredKeyBehaviorRuntime Runtime(params KeyBehaviorBinding[] behaviors)
        => new(new KeyBehaviorProfile(behaviors, [
            new KeyBehaviorLayer("NAV", [
                new KeyBehaviorLayerBinding(KeyCode.H, KeyBehaviorAction.Key("Left"))
            ])
        ]));

    private static KeyBehaviorBinding Behavior(KeyCode key, KeyBehaviorAction tap, KeyBehaviorAction hold)
        => new(key, tap, hold, timeoutMs: 180, interrupt: TapHoldInterruptPolicy.Hold);
}
