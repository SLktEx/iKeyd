using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class OneShotLayerBehaviorRoutingTests
{
    [Fact]
    public void Tap_applies_layer_to_next_key_lifecycle_then_expires()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "OSL", "NUM")]),
                Keymap("NUM", singles: [new SingleMapping<string>("B", "num-b")])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 21)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 22)));

        Assert.Equal(["num-b", "num-b"], keyboard.Text);
        Assert.Empty(fallback.Events);

        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 30)));
        Assert.Single(fallback.Events);
    }

    [Fact]
    public void Different_key_pressed_while_consumed_key_is_held_does_not_inherit_one_shot()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "OSL", "NUM")]),
                Keymap(
                    "NUM",
                    singles:
                    [
                        new SingleMapping<string>("B", "num-b"),
                        new SingleMapping<string>("C", "num-c")
                    ])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 21)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('C', KeyEventKind.Up, 22)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 23)));

        Assert.Equal(["num-b"], keyboard.Text);
        Assert.Equal(2, fallback.Events.Count);
    }

    [Fact]
    public void Newly_armed_one_shot_survives_cleanup_of_previous_consumed_key()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "OSL", "NUM")]),
                Keymap("NUM", behaviors: [Behavior("B", "OSL", "NAV")]),
                Keymap("NAV", singles: [new SingleMapping<string>("C", "nav-c")])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);
        Press(router, 'B', 20);
        Press(router, 'C', 40);

        Assert.Equal(["nav-c"], keyboard.Text);
        Assert.Empty(fallback.Events);

        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 60)));
        Assert.Single(fallback.Events);
    }

    [Fact]
    public void Transparent_next_key_still_consumes_one_shot()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "OSL", "NUM")]),
                Keymap("NUM", singles: [new SingleMapping<string>("B", "num-b")])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);

        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('C', KeyEventKind.Up, 21)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 30)));

        Assert.Empty(keyboard.Text);
        Assert.Equal(3, fallback.Events.Count);
    }

    [Fact]
    public void Held_OSL_is_momentary_for_multiple_keys_and_does_not_arm_after_interrupt()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "OSL", "NUM")]),
                Keymap(
                    "NUM",
                    singles:
                    [
                        new SingleMapping<string>("B", "num-b"),
                        new SingleMapping<string>("C", "num-c")
                    ])
            ],
            keyboard,
            fallback);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Press(router, 'B', 10);
        Press(router, 'C', 20);
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 30)));

        Assert.Equal(["num-b", "num-c"], keyboard.Text);
        Assert.Empty(fallback.Events);

        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 40)));
        Assert.Single(fallback.Events);
    }

    [Fact]
    public void Reset_clears_armed_one_shot()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "OSL", "NUM")]),
                Keymap("NUM", singles: [new SingleMapping<string>("B", "num-b")])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);
        router.ResetInputState();

        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20)));
        Assert.Empty(keyboard.Text);
        Assert.Single(fallback.Events);
    }

    private static BehaviorWindowsInputRouter Router(
        IEnumerable<AutomationKeymapProfile> keymaps,
        RecordingKeyboardOutput keyboard,
        RecordingHandler fallback)
    {
        var all = keymaps.ToList();
        if (!all.Any(keymap => keymap.Name.Equals("K", StringComparison.OrdinalIgnoreCase)))
            all.Add(Keymap("K"));
        var profile = new AutomationProfile(40, all);
        return new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback);
    }

    private static AutomationKeymapProfile Keymap(
        string name,
        IEnumerable<SingleMapping<string>>? singles = null,
        IEnumerable<BehaviorMappingProfile>? behaviors = null)
        => new(name, singles ?? [], [], behaviors ?? []);

    private static BehaviorMappingProfile Behavior(string key, string name, string argument)
        => new(key, new BehaviorInvocationProfile(name, [argument]));

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

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<string> Text { get; } = [];
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class RecordingHandler : IKeyboardEventHandler
    {
        public List<KeyboardEvent> Events { get; } = [];

        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            Events.Add(keyboardEvent);
            return KeyboardDisposition.PassThrough;
        }
    }
}
