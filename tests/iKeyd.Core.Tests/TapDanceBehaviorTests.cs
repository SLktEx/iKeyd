using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class TapDanceBehaviorTests
{
    [Fact]
    public void Single_tap_resolves_at_inclusive_deadline()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, Dance("X", "Y"));

        Assert.True(runtime.OnKeyDown(source, 0).Suppress);
        Assert.Empty(runtime.OnKeyUp(source, 10).Actions);
        Assert.Equal(1, runtime.PendingCount);
        Assert.Equal(210, runtime.NextDeadlineMs);

        Assert.Empty(runtime.AdvanceTo(209));
        Assert.Equal(
            [BehaviorAction.SendKey(new KeyId("X"))],
            runtime.AdvanceTo(210));
        Assert.Equal(0, runtime.PendingCount);
        Assert.Null(runtime.NextDeadlineMs);
    }

    [Fact]
    public void Double_tap_resumes_same_instance_and_resolves_immediately_at_max_count()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, Dance("X", "Y"));

        runtime.OnKeyDown(source, 0);
        runtime.OnKeyUp(source, 10);
        Assert.Equal(1, runtime.PendingCount);

        Assert.Empty(runtime.OnKeyDown(source, 100).Actions);
        Assert.Equal(1, runtime.ActiveCount);
        Assert.Equal(0, runtime.PendingCount);

        var secondRelease = runtime.OnKeyUp(source, 110);

        Assert.Equal([BehaviorAction.SendKey(new KeyId("Y"))], secondRelease.Actions);
        Assert.Equal(0, runtime.ActiveCount);
        Assert.Equal(0, runtime.PendingCount);
        Assert.Null(runtime.NextDeadlineMs);
    }

    [Fact]
    public void Intermediate_tap_rearms_deadline_and_resolves_its_count_on_timeout()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, Dance("X", "Y", "Z"));

        runtime.OnKeyDown(source, 0);
        runtime.OnKeyUp(source, 10);
        runtime.OnKeyDown(source, 100);
        var secondRelease = runtime.OnKeyUp(source, 110);

        Assert.Empty(secondRelease.Actions);
        Assert.Equal(310, runtime.NextDeadlineMs);
        Assert.Equal(
            [BehaviorAction.SendKey(new KeyId("Y"))],
            runtime.AdvanceTo(310));
        Assert.Equal(0, runtime.PendingCount);
    }

    [Fact]
    public void Unrelated_key_interrupt_resolves_pending_count_before_new_key_starts()
    {
        var source = new KeyId("A");
        var other = new KeyId("B");
        var runtime = new BehaviorRuntime(
            new Dictionary<KeyId, BehaviorDefinition>
            {
                [source] = Dance("X", "Y"),
                [other] = StandardBehaviors.Press(BehaviorAction.SendKey(new KeyId("Q")))
            });

        runtime.OnKeyDown(source, 0);
        runtime.OnKeyUp(source, 10);

        var down = runtime.OnKeyDown(other, 100);

        Assert.Equal(
            [
                BehaviorAction.SendKey(new KeyId("X")),
                BehaviorAction.SendKey(new KeyId("Q"))
            ],
            down.Actions);
        Assert.Equal(0, runtime.PendingCount);
    }

    [Fact]
    public void Interruption_during_second_press_resolves_two_taps_without_releasing_twice()
    {
        var source = new KeyId("A");
        var other = new KeyId("B");
        var runtime = Runtime(source, Dance("X", "Y", "Z"));

        runtime.OnKeyDown(source, 0);
        runtime.OnKeyUp(source, 10);
        runtime.OnKeyDown(source, 100);

        var interrupt = runtime.ObserveKeyDown(other, 120);
        var release = runtime.OnKeyUp(source, 130);

        Assert.Equal([BehaviorAction.SendKey(new KeyId("Y"))], interrupt.Actions);
        Assert.Empty(release.Actions);
        Assert.Equal(0, runtime.PendingCount);
    }

    [Fact]
    public void Physical_repeat_does_not_increment_tap_count()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, Dance("X", "Y"));

        runtime.OnKeyDown(source, 0);
        Assert.Empty(runtime.OnKeyDown(source, 5).Actions);
        runtime.OnKeyUp(source, 10);

        Assert.Equal(
            [BehaviorAction.SendKey(new KeyId("X"))],
            runtime.AdvanceTo(210));
    }

    [Fact]
    public void Cancellation_discards_pending_sequence_without_output()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, Dance("X", "Y"));

        runtime.OnKeyDown(source, 0);
        runtime.OnKeyUp(source, 10);

        Assert.Empty(runtime.CancelAll());
        Assert.Equal(0, runtime.PendingCount);
        Assert.Null(runtime.NextDeadlineMs);
    }

    [Fact]
    public void Same_source_press_at_expired_deadline_finishes_old_sequence_then_starts_new_one()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, Dance("X", "Y"));

        runtime.OnKeyDown(source, 0);
        runtime.OnKeyUp(source, 10);

        var downAtDeadline = runtime.OnKeyDown(source, 210);

        Assert.Equal([BehaviorAction.SendKey(new KeyId("X"))], downAtDeadline.Actions);
        Assert.Equal(1, runtime.ActiveCount);
        Assert.Equal(0, runtime.PendingCount);

        runtime.OnKeyUp(source, 220);
        Assert.Equal(
            [BehaviorAction.SendKey(new KeyId("X"))],
            runtime.AdvanceTo(420));
    }

    [Fact]
    public void Zero_tapping_term_resolves_on_release_without_retention()
    {
        var source = new KeyId("A");
        var runtime = Runtime(
            source,
            StandardBehaviors.TD(
                [new KeyId("X"), new KeyId("Y")],
                new TapDanceOptions { TappingTermMs = 0 }));

        runtime.OnKeyDown(source, 0);
        var release = runtime.OnKeyUp(source, 1);

        Assert.Equal([BehaviorAction.SendKey(new KeyId("X"))], release.Actions);
        Assert.Equal(0, runtime.PendingCount);
        Assert.Null(runtime.NextDeadlineMs);
    }

    [Fact]
    public void Factory_accepts_two_to_eight_outputs_and_tapping_term()
    {
        var definition = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile(
                "TD",
                ["A", "B", "C", "D", "E", "F", "G", "H"],
                new Dictionary<string, string>
                {
                    ["tapping_term"] = "175ms"
                }));

        Assert.NotNull(definition);
    }

    [Fact]
    public void Factory_rejects_unbounded_shapes_and_unknown_options()
    {
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile("TD", ["A"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(
                new BehaviorInvocationProfile(
                    "TD",
                    ["A", "B", "C", "D", "E", "F", "G", "H", "I"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(
                new BehaviorInvocationProfile(
                    "TD",
                    ["A", "B"],
                    new Dictionary<string, string> { ["hold_on_other_key_press"] = "true" })));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(
                new BehaviorInvocationProfile(
                    "TD",
                    ["A", "B"],
                    new Dictionary<string, string> { ["tapping_term"] = "forever" })));
    }

    private static BehaviorDefinition Dance(params string[] outputs)
        => StandardBehaviors.TD(
            outputs.Select(value => new KeyId(value)),
            new TapDanceOptions { TappingTermMs = 200 });

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
