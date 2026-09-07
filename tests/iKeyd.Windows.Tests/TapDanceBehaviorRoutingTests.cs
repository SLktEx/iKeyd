using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class TapDanceBehaviorRoutingTests
{
    [Fact]
    public void Single_tap_emits_first_output_when_idle_deadline_fires()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = BaseRouter(keyboard, fallback, scheduler);

        Press(router, 'A', 0);

        Assert.Empty(events);
        Assert.Equal(201, scheduler.DeadlineMs);

        scheduler.Fire(201);

        Assert.Equal([PressOutput('X')], events);
        Assert.Null(scheduler.DeadlineMs);
    }

    [Fact]
    public void Double_tap_emits_second_output_on_second_release_without_waiting_again()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = BaseRouter(keyboard, fallback, scheduler);

        Press(router, 'A', 0);
        Assert.Equal(201, scheduler.DeadlineMs);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 100)));
        Assert.Null(scheduler.DeadlineMs);
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 101)));

        Assert.Equal([PressOutput('Y')], events);
        Assert.Null(scheduler.DeadlineMs);
    }

    [Fact]
    public void Other_key_resolves_pending_dance_before_fallback_key_down()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = BaseRouter(keyboard, fallback, scheduler);

        Press(router, 'A', 0);
        events.Clear();

        Assert.Equal(
            KeyboardDisposition.PassThrough,
            router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 100)));

        Assert.Equal(
            [
                PressOutput('X'),
                Fallback('B', KeyEventKind.Down)
            ],
            events);
        Assert.Null(scheduler.DeadlineMs);
    }

    [Fact]
    public void Dance_started_from_one_shot_layer_keeps_its_runtime_for_followup_tap()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = OneShotLayerRouter(keyboard, fallback, scheduler);

        // Tap C to arm K for one physical key lifecycle.
        Press(router, 'C', 0);
        events.Clear();

        // First A starts TD(X,Y) from K and consumes the one-shot layer.
        Press(router, 'A', 20);
        Assert.Empty(events);
        Assert.Equal(221, scheduler.DeadlineMs);

        // K is no longer active here. The retained TD instance must win over the
        // BASE A = TEXT("base") binding and receive the second tap itself.
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 100)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 101)));

        Assert.Equal([PressOutput('Y')], events);
        Assert.DoesNotContain("text:base", events);
        Assert.Null(scheduler.DeadlineMs);
    }

    [Fact]
    public void Reset_discards_pending_dance_without_output()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = BaseRouter(keyboard, fallback, scheduler);

        Press(router, 'A', 0);
        Assert.Equal(201, scheduler.DeadlineMs);

        router.ResetInputState();

        Assert.Empty(events);
        Assert.Null(scheduler.DeadlineMs);
    }

    private static BehaviorWindowsInputRouter BaseRouter(
        RecordingKeyboardOutput keyboard,
        RecordingHandler fallback,
        IBehaviorDeadlineScheduler scheduler)
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [
                        new BehaviorMappingProfile(
                            "A",
                            new BehaviorInvocationProfile(
                                "TD",
                                ["X", "Y"],
                                new Dictionary<string, string>
                                {
                                    ["tapping_term"] = "200ms"
                                }))
                    ]),
                new AutomationKeymapProfile("K", [], [])
            ]);

        return new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            behaviorDeadlineScheduler: scheduler);
    }

    private static BehaviorWindowsInputRouter OneShotLayerRouter(
        RecordingKeyboardOutput keyboard,
        RecordingHandler fallback,
        IBehaviorDeadlineScheduler scheduler)
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [
                        new BehaviorMappingProfile(
                            "C",
                            new BehaviorInvocationProfile("OSL", ["K"])),
                        new BehaviorMappingProfile(
                            "A",
                            new BehaviorInvocationProfile("TEXT", ["base"]))
                    ]),
                new AutomationKeymapProfile(
                    "K",
                    [],
                    [],
                    [
                        new BehaviorMappingProfile(
                            "A",
                            new BehaviorInvocationProfile(
                                "TD",
                                ["X", "Y"],
                                new Dictionary<string, string>
                                {
                                    ["tapping_term"] = "200ms"
                                }))
                    ])
            ]);

        return new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            behaviorDeadlineScheduler: scheduler);
    }

    private static void Press(BehaviorWindowsInputRouter router, ushort key, long timestampMs)
    {
        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical(key, KeyEventKind.Down, timestampMs)));
        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical(key, KeyEventKind.Up, timestampMs + 1)));
    }

    private static KeyboardEvent Physical(ushort virtualKey, KeyEventKind kind, long timestampMs)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestampMs);

    private static string PressOutput(ushort virtualKey)
        => $"press:{virtualKey}";

    private static string Fallback(ushort virtualKey, KeyEventKind kind)
        => $"fallback:{virtualKey}:{kind}";

    private sealed class RecordingKeyboardOutput(List<string> events) : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => events.Add($"output:{key.VirtualKey}:{kind}");

        public void SendKeyPress(KeyboardKey key)
            => events.Add(PressOutput(key.VirtualKey));

        public void SendText(string text)
            => events.Add($"text:{text}");

        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class RecordingHandler(List<string> events) : IKeyboardEventHandler
    {
        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            events.Add(Fallback(keyboardEvent.Key.VirtualKey, keyboardEvent.Kind));
            return KeyboardDisposition.PassThrough;
        }
    }

    private sealed class FakeBehaviorDeadlineScheduler : IBehaviorDeadlineScheduler
    {
        private Action<long>? _callback;

        public long? DeadlineMs { get; private set; }

        public void Schedule(long? deadlineMs, Action<long> callback)
        {
            DeadlineMs = deadlineMs;
            _callback = deadlineMs is null ? null : callback;
        }

        public void Fire(long timestampMs)
        {
            if (DeadlineMs is not long deadline)
                throw new InvalidOperationException("No deadline is scheduled.");
            if (timestampMs < deadline)
                throw new ArgumentOutOfRangeException(nameof(timestampMs));

            var callback = _callback ?? throw new InvalidOperationException("No callback is registered.");
            DeadlineMs = null;
            _callback = null;
            callback(timestampMs);
        }

        public void Dispose()
        {
            DeadlineMs = null;
            _callback = null;
        }
    }
}
