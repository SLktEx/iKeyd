using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class BehaviorDeadlineRoutingTests
{
    [Fact]
    public void Idle_deadline_resolves_MT_hold_without_waiting_for_another_input_event()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = Router(keyboard, scheduler);

        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 1_000)));
        Assert.Equal(1_100, scheduler.DeadlineMs);
        Assert.Empty(events);

        scheduler.Fire(1_100);

        Assert.Equal([Output(WindowsKeyMap.Control, KeyEventKind.Down)], events);
        Assert.Null(scheduler.DeadlineMs);

        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 1_200)));
        Assert.Equal(
            [
                Output(WindowsKeyMap.Control, KeyEventKind.Down),
                Output(WindowsKeyMap.Control, KeyEventKind.Up)
            ],
            events);
    }

    [Fact]
    public void Tap_before_deadline_cancels_wakeup_and_emits_tap_key()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = Router(keyboard, scheduler);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 1_000));
        Assert.Equal(1_100, scheduler.DeadlineMs);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 1_050));

        Assert.Equal([Press('X')], events);
        Assert.Null(scheduler.DeadlineMs);
    }

    [Fact]
    public void Reset_cancels_pending_deadline_without_resolving_hold()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        using var router = Router(keyboard, scheduler);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 1_000));
        Assert.Equal(1_100, scheduler.DeadlineMs);

        router.ResetInputState();

        Assert.Null(scheduler.DeadlineMs);
        Assert.Empty(events);
    }

    [Fact]
    public void Router_disposes_deadline_scheduler()
    {
        var keyboard = new RecordingKeyboardOutput([]);
        var scheduler = new FakeBehaviorDeadlineScheduler();
        var router = Router(keyboard, scheduler);

        router.Dispose();

        Assert.True(scheduler.IsDisposed);
        Assert.Null(scheduler.DeadlineMs);
    }

    private static BehaviorWindowsInputRouter Router(
        RecordingKeyboardOutput keyboard,
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
                                "MT",
                                ["Ctrl", "X"],
                                new Dictionary<string, string>
                                {
                                    ["tapping_term"] = "100ms"
                                }))
                    ]),
                new AutomationKeymapProfile("K", [], [])
            ]);

        return new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            new PassThroughHandler(),
            behaviorDeadlineScheduler: scheduler);
    }

    private static KeyboardEvent Physical(ushort virtualKey, KeyEventKind kind, long timestampMs)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestampMs);

    private static string Output(ushort virtualKey, KeyEventKind kind)
        => $"output:{virtualKey}:{kind}";

    private static string Press(ushort virtualKey)
        => $"press:{virtualKey}";

    private sealed class RecordingKeyboardOutput(List<string> events) : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => events.Add(Output(key.VirtualKey, kind));

        public void SendKeyPress(KeyboardKey key)
            => events.Add(Press(key.VirtualKey));

        public void SendText(string text)
            => events.Add($"text:{text}");

        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class PassThroughHandler : IKeyboardEventHandler
    {
        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
            => KeyboardDisposition.PassThrough;
    }

    private sealed class FakeBehaviorDeadlineScheduler : IBehaviorDeadlineScheduler
    {
        private Action<long>? _callback;

        public long? DeadlineMs { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Schedule(long? deadlineMs, Action<long> callback)
        {
            if (IsDisposed)
                return;

            DeadlineMs = deadlineMs;
            _callback = deadlineMs is null ? null : callback;
        }

        public void Fire(long timestampMs)
        {
            if (DeadlineMs is not long deadline)
                throw new InvalidOperationException("No deadline is scheduled.");
            if (timestampMs < deadline)
                throw new ArgumentOutOfRangeException(nameof(timestampMs), "Cannot fire before the scheduled deadline.");

            var callback = _callback ?? throw new InvalidOperationException("No deadline callback is registered.");
            DeadlineMs = null;
            _callback = null;
            callback(timestampMs);
        }

        public void Dispose()
        {
            IsDisposed = true;
            DeadlineMs = null;
            _callback = null;
        }
    }
}
