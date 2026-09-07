using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class BehaviorDeadlineTests
{
    [Fact]
    public void Standard_tap_hold_publishes_and_clears_absolute_deadline()
    {
        var source = new KeyId("A");
        var runtime = Runtime(
            source,
            StandardBehaviors.MT(
                "Control",
                new KeyId("X"),
                new ModTapOptions { TappingTermMs = 100 }));

        runtime.OnKeyDown(source, 1_000);

        Assert.Equal(1_100, runtime.NextDeadlineMs);
        Assert.Empty(runtime.AdvanceTo(1_099));
        Assert.Equal(1_100, runtime.NextDeadlineMs);

        Assert.Equal(
            [BehaviorAction.ModifierDown("Control")],
            runtime.AdvanceTo(1_100));
        Assert.Null(runtime.NextDeadlineMs);

        Assert.Equal(
            [BehaviorAction.ModifierUp("Control")],
            runtime.OnKeyUp(source, 1_200).Actions);
    }

    [Fact]
    public void Releasing_before_deadline_cancels_wakeup_need()
    {
        var source = new KeyId("A");
        var runtime = Runtime(
            source,
            StandardBehaviors.LT(
                "NUM",
                new KeyId("X"),
                new LayerTapOptions { TappingTermMs = 100 }));

        runtime.OnKeyDown(source, 10);
        var release = runtime.OnKeyUp(source, 50);

        Assert.Equal([BehaviorAction.SendKey(new KeyId("X"))], release.Actions);
        Assert.Null(runtime.NextDeadlineMs);
    }

    [Fact]
    public void Custom_tap_hold_uses_same_deadline_contract()
    {
        var source = new KeyId("A");
        var definition = new UserBehaviorDefinitionProfile(
            "SMART",
            [],
            handlers:
            [
                new UserBehaviorHandlerProfile(
                    "hold",
                    [],
                    [new UserBehaviorStatementProfile("modifier_down", value: "Ctrl")])
            ]);
        var behavior = BehaviorDefinitionFactory.Create(
            new BehaviorInvocationProfile(
                "SMART",
                [],
                new Dictionary<string, string>
                {
                    ["tapping_term"] = "75ms"
                }),
            new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["SMART"] = definition
            });
        var runtime = Runtime(source, behavior);

        runtime.OnKeyDown(source, 500);
        Assert.Equal(575, runtime.NextDeadlineMs);

        Assert.Equal(
            [BehaviorAction.ModifierDown("Ctrl")],
            runtime.AdvanceTo(575));
        Assert.Null(runtime.NextDeadlineMs);

        Assert.Equal(
            [BehaviorAction.ModifierUp("Ctrl")],
            runtime.OnKeyUp(source, 600).Actions);
    }

    [Fact]
    public void Cancellation_clears_deadline_without_resolving_hold()
    {
        var source = new KeyId("A");
        var runtime = Runtime(
            source,
            StandardBehaviors.MT(
                "Shift",
                new KeyId("X"),
                new ModTapOptions { TappingTermMs = 100 }));

        runtime.OnKeyDown(source, 0);
        Assert.Equal(100, runtime.NextDeadlineMs);

        Assert.Empty(runtime.CancelAll());
        Assert.Null(runtime.NextDeadlineMs);
    }

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
