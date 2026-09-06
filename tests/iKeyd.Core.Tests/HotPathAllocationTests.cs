using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Keymaps;
using iKeyd.Core.Runtime;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class HotPathAllocationTests
{
    [Fact]
    public void Chord_engine_try_path_allocates_zero_bytes_after_warmup()
    {
        var keymap = new Keymap<string>(
            [
                new SingleMapping<string>(KeyCode.Q, "q"),
                new SingleMapping<string>(KeyCode.W, "w"),
                new SingleMapping<string>(KeyCode.E, "e")
            ],
            [new ChordMapping<string>(KeyCode.Q, KeyCode.W, "qw")]);
        var engine = new ChordEngine<string>(keymap, 40);

        long timestamp = 0;
        for (var i = 0; i < 100; i++)
            timestamp = ExerciseEngine(engine, timestamp);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        for (var i = 0; i < 10_000; i++)
        {
            timestamp = ExerciseEngine(engine, timestamp);
            checksum += engine.State == ChordEngineState.Idle ? 1 : 0;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(10_000, checksum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Layer_state_machine_allocates_zero_bytes_after_warmup()
    {
        var state = LayerRuntimeState.Empty;
        for (var i = 0; i < 100; i++)
            state = ExerciseLayers(state);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        for (var i = 0; i < 10_000; i++)
        {
            state = ExerciseLayers(state);
            checksum += state.Layers.Count;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, checksum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Configured_tap_hold_runtime_allocates_zero_bytes_after_warmup()
    {
        var profile = new KeyBehaviorProfile(
            behaviors:
            [
                new KeyBehaviorBinding(
                    KeyCode.A,
                    KeyBehaviorAction.Key("A"),
                    KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control)),
                new KeyBehaviorBinding(
                    KeyCode.Space,
                    KeyBehaviorAction.Key("Space"),
                    KeyBehaviorAction.Layer("NAV"))
            ],
            layers:
            [
                new KeyBehaviorLayer(
                    "NAV",
                    [new KeyBehaviorLayerBinding(KeyCode.H, KeyBehaviorAction.Key("Left"))])
            ]);
        var runtime = new ConfiguredKeyBehaviorRuntime(profile);

        long timestamp = 0;
        for (var i = 0; i < 100; i++)
            timestamp = ExerciseBehaviors(runtime, timestamp);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        for (var i = 0; i < 10_000; i++)
        {
            timestamp = ExerciseBehaviors(runtime, timestamp);
            checksum += runtime.ActiveHoldCount;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, checksum);
        Assert.Equal(0, allocated);
    }

    private static long ExerciseEngine(ChordEngine<string> engine, long timestamp)
    {
        // Successful chord: Q + W.
        AssertNoOutput(engine.TryOnKeyDown(KeyCode.Q, timestamp, out _));
        AssertOutput(engine.TryOnKeyDown(KeyCode.W, timestamp + 1, out var chord), chord, "qw");

        // Failed chord: Q + E resolves Q and leaves E pending.
        AssertNoOutput(engine.TryOnKeyDown(KeyCode.Q, timestamp + 2, out _));
        AssertOutput(engine.TryOnKeyDown(KeyCode.E, timestamp + 3, out var single), single, "q");

        // Timeout resolves the pending E without a collection allocation.
        AssertOutput(engine.TryAdvanceTo(timestamp + 44, out var timedOut), timedOut, "e");
        return timestamp + 100;
    }

    private static LayerRuntimeState ExerciseLayers(LayerRuntimeState state)
    {
        var transition = LayerStateMachine.Apply(state, LayerEvent.MDown);
        state = transition.State;

        transition = LayerStateMachine.Apply(state, LayerEvent.HDown);
        state = transition.State;

        transition = LayerStateMachine.Apply(state, LayerEvent.HUp);
        state = transition.State;
        if (transition.Actions.Count != 1 || transition.Actions[0] != LayerAction.Tab)
            throw new InvalidOperationException("Expected MH release to produce Tab.");

        transition = LayerStateMachine.Apply(state, LayerEvent.MUp);
        state = transition.State;
        if (transition.Actions.Count != 0 || state.Layers.Count != 0)
            throw new InvalidOperationException("Layer state did not return to empty.");

        return state;
    }

    private static long ExerciseBehaviors(ConfiguredKeyBehaviorRuntime runtime, long timestamp)
    {
        var down = runtime.OnKeyDown(KeyCode.A, timestamp);
        if (!down.Consumed || down.Transitions.Count != 0)
            throw new InvalidOperationException("Expected A mod-tap to start pending.");

        var interrupt = runtime.OnKeyDown(KeyCode.Q, timestamp + 1);
        if (interrupt.Consumed || interrupt.Transitions.Count != 1 ||
            interrupt.Transitions[0].Kind != KeyBehaviorTransitionKind.HoldStarted)
        {
            throw new InvalidOperationException("Expected Q to resolve A as a Control hold and continue.");
        }

        var unrelatedUp = runtime.OnKeyUp(KeyCode.Q, timestamp + 2);
        if (unrelatedUp.Consumed || unrelatedUp.Transitions.Count != 0)
            throw new InvalidOperationException("Unexpected behavior handling for unrelated Q release.");

        var holdUp = runtime.OnKeyUp(KeyCode.A, timestamp + 3);
        if (!holdUp.Consumed || holdUp.Transitions.Count != 1 ||
            holdUp.Transitions[0].Kind != KeyBehaviorTransitionKind.HoldEnded)
        {
            throw new InvalidOperationException("Expected A release to end the Control hold.");
        }

        var tapDown = runtime.OnKeyDown(KeyCode.Space, timestamp + 10);
        var tapUp = runtime.OnKeyUp(KeyCode.Space, timestamp + 20);
        if (!tapDown.Consumed || tapDown.Transitions.Count != 0 ||
            !tapUp.Consumed || tapUp.Transitions.Count != 1 ||
            tapUp.Transitions[0].Kind != KeyBehaviorTransitionKind.Tap)
        {
            throw new InvalidOperationException("Expected Space layer-tap to resolve as a tap.");
        }

        return timestamp + 100;
    }

    private static void AssertNoOutput(bool hasOutput)
    {
        if (hasOutput)
            throw new InvalidOperationException("Unexpected chord-engine output during allocation measurement.");
    }

    private static void AssertOutput(bool hasOutput, string? actual, string expected)
    {
        if (!hasOutput || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected chord-engine output; expected '{expected}', got '{actual}'.");
    }
}
