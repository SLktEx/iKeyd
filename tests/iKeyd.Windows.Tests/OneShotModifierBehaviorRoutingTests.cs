using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class OneShotModifierBehaviorRoutingTests
{
    [Fact]
    public void Tap_wraps_next_fallback_key_lifecycle_in_modifier_down_and_up()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        using var router = Router(keyboard, fallback);

        Press(router, 'A', 0);
        events.Clear();

        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 21)));

        Assert.Equal(
            [
                Output(WindowsKeyMap.Control, KeyEventKind.Down),
                Fallback('B', KeyEventKind.Down),
                Fallback('B', KeyEventKind.Up),
                Output(WindowsKeyMap.Control, KeyEventKind.Up)
            ],
            events);

        events.Clear();
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 30)));
        Assert.Equal([Fallback('C', KeyEventKind.Down)], events);
    }

    [Fact]
    public void Physical_repeat_does_not_repress_or_release_modifier_early()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        using var router = Router(keyboard, fallback);

        Press(router, 'A', 0);
        events.Clear();

        router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20));
        router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 21));
        router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 22));

        Assert.Equal(
            [
                Output(WindowsKeyMap.Control, KeyEventKind.Down),
                Fallback('B', KeyEventKind.Down),
                Fallback('B', KeyEventKind.Down),
                Fallback('B', KeyEventKind.Up),
                Output(WindowsKeyMap.Control, KeyEventKind.Up)
            ],
            events);
    }

    [Fact]
    public void Interrupted_hold_is_plain_modifier_hold_and_does_not_arm_after_release()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        using var router = Router(keyboard, fallback);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 10)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 11)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 20)));

        Assert.Equal(
            [
                Output(WindowsKeyMap.Control, KeyEventKind.Down),
                Fallback('B', KeyEventKind.Down),
                Fallback('B', KeyEventKind.Up),
                Output(WindowsKeyMap.Control, KeyEventKind.Up)
            ],
            events);

        events.Clear();
        router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 30));
        Assert.Equal([Fallback('C', KeyEventKind.Down)], events);
    }

    [Fact]
    public void Reset_clears_armed_modifier_without_sending_phantom_key_up()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        using var router = Router(keyboard, fallback);

        Press(router, 'A', 0);
        events.Clear();

        router.ResetInputState();
        Assert.Empty(events);

        router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20));
        Assert.Equal([Fallback('B', KeyEventKind.Down)], events);
    }

    [Fact]
    public void Reset_releases_modifier_when_consumed_target_is_still_held()
    {
        var events = new List<string>();
        var keyboard = new RecordingKeyboardOutput(events);
        var fallback = new RecordingHandler(events);
        using var router = Router(keyboard, fallback);

        Press(router, 'A', 0);
        events.Clear();

        router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20));
        router.ResetInputState();

        Assert.Equal(
            [
                Output(WindowsKeyMap.Control, KeyEventKind.Down),
                Fallback('B', KeyEventKind.Down),
                Output(WindowsKeyMap.Control, KeyEventKind.Up)
            ],
            events);
    }

    private static BehaviorWindowsInputRouter Router(
        RecordingKeyboardOutput keyboard,
        RecordingHandler fallback)
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("OSM", ["Ctrl"]))]),
                new AutomationKeymapProfile("K", [], [])
            ]);

        return new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback);
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

    private static string Output(ushort virtualKey, KeyEventKind kind)
        => $"output:{virtualKey}:{kind}";

    private static string Fallback(ushort virtualKey, KeyEventKind kind)
        => $"fallback:{virtualKey}:{kind}";

    private sealed class RecordingKeyboardOutput(List<string> events) : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => events.Add(Output(key.VirtualKey, kind));

        public void SendKeyPress(KeyboardKey key)
            => events.Add($"press:{key.VirtualKey}");

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
}
