using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class PersistentLayerBehaviorTests
{
    [Fact]
    public void TG_latches_layer_until_toggled_off()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "TG", "NUM")]),
                Keymap(
                    "NUM",
                    singles: [new SingleMapping<string>("B", "num-b")],
                    behaviors: [Behavior("A", "TG", "NUM")])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);
        Press(router, 'B', 20);

        Assert.Equal(["num-b"], keyboard.Text);
        Assert.Empty(fallback.Events);

        Press(router, 'A', 40);
        var disposition = router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 60));

        Assert.Equal(KeyboardDisposition.PassThrough, disposition);
        Assert.Single(fallback.Events);
        Assert.Equal((ushort)'B', fallback.Events[0].Key.VirtualKey);
    }

    [Fact]
    public void TO_replaces_the_persistent_layer_selection()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "TG", "NAV")]),
                Keymap(
                    "NAV",
                    singles: [new SingleMapping<string>("B", "nav-b")],
                    behaviors: [Behavior("C", "TO", "NUM")]),
                Keymap("NUM", singles: [new SingleMapping<string>("D", "num-d")])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);
        Press(router, 'C', 20);
        Press(router, 'D', 40);
        var oldLayer = router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 60));

        Assert.Equal(["num-d"], keyboard.Text);
        Assert.Equal(KeyboardDisposition.PassThrough, oldLayer);
        Assert.Single(fallback.Events);
        Assert.Equal((ushort)'B', fallback.Events[0].Key.VirtualKey);
    }

    [Fact]
    public void Persistent_layer_changes_do_not_consume_momentary_layer_ownership()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "MO", "NAV")]),
                Keymap(
                    "NAV",
                    singles: [new SingleMapping<string>("B", "nav-b")],
                    behaviors: [Behavior("C", "TG", "NUM")]),
                Keymap("NUM", singles: [new SingleMapping<string>("B", "num-b")])
            ],
            keyboard,
            fallback);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0));
        Press(router, 'C', 10);
        Press(router, 'B', 30);
        router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 50));
        Press(router, 'B', 60);

        Assert.Equal(["nav-b", "num-b"], keyboard.Text);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Reset_clears_persistent_layer_selection()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(
            [
                Keymap("S", behaviors: [Behavior("A", "TG", "NUM")]),
                Keymap("NUM", singles: [new SingleMapping<string>("B", "num-b")])
            ],
            keyboard,
            fallback);

        Press(router, 'A', 0);
        router.ResetInputState();
        var disposition = router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20));

        Assert.Equal(KeyboardDisposition.PassThrough, disposition);
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
