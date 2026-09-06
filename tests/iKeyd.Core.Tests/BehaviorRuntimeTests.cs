using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
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
    public void Profile_LT_options_change_runtime_resolution()
    {
        var source = new KeyId("A");
        var invocation = new BehaviorInvocationProfile(
            "LT",
            ["NUM", "Z"],
            new Dictionary<string, string>
            {
                ["tapping_term"] = "170ms",
                ["hold_on_other_key_press"] = "false"
            });
        var runtime = CreateRuntime(source, invocation.BuildDefinition());

        runtime.OnKeyDown(source, 0);
        var interrupt = runtime.OnKeyDown(new KeyId("B"), 100);
        var hold = runtime.AdvanceTo(170);
        var up = runtime.OnKeyUp(source, 171);

        Assert.Empty(interrupt.Actions);
        Assert.Equal([BehaviorAction.LayerOn("NUM")], hold);
        Assert.Equal([BehaviorAction.LayerOff("NUM")], up.Actions);
    }

    [Fact]
    public void MT_tap_emits_tap_key()
    {
        var source = new KeyId("A");
        var tap = new KeyId("X");
        var runtime = CreateRuntime(source, StandardBehaviors.MT("Ctrl", tap));

        runtime.OnKeyDown(source, 0);
        var up = runtime.OnKeyUp(source, 100);

        Assert.Equal([BehaviorAction.SendKey(tap)], up.Actions);
    }

    [Fact]
    public void MT_interrupt_holds_modifier_until_source_release()
    {
        var source = new KeyId("A");
        var runtime = CreateRuntime(source, StandardBehaviors.MT("Ctrl", new KeyId("X")));

        runtime.OnKeyDown(source, 0);
        var interrupt = runtime.OnKeyDown(new KeyId("B"), 50);
        var up = runtime.OnKeyUp(source, 60);

        Assert.Equal([BehaviorAction.ModifierDown("Ctrl")], interrupt.Actions);
        Assert.Equal([BehaviorAction.ModifierUp("Ctrl")], up.Actions);
    }

    [Fact]
    public void MT_cancel_releases_modifier_after_hold()
    {
        var source = new KeyId("A");
        var runtime = CreateRuntime(source, StandardBehaviors.MT("Shift", new KeyId("X")));

        runtime.OnKeyDown(source, 0);
        runtime.OnKeyDown(new KeyId("B"), 50);

        Assert.Equal([BehaviorAction.ModifierUp("Shift")], runtime.CancelAll());
    }

    [Fact]
    public void Unknown_tap_hold_option_is_rejected()
    {
        var invocation = new BehaviorInvocationProfile(
            "LT",
            ["NUM", "Z"],
            new Dictionary<string, string> { ["mystery"] = "true" });

        var error = Assert.Throws<InvalidDataException>(() => invocation.BuildDefinition());
        Assert.Contains("mystery", error.Message, StringComparison.OrdinalIgnoreCase);
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
