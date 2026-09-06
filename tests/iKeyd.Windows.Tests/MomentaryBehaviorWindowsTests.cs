using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class MomentaryBehaviorWindowsTests
{
    [Fact]
    public void MO_activates_layer_for_following_key_and_releases_on_key_up()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("MO", ["NAV"]))]),
                new AutomationKeymapProfile("NAV", [new SingleMapping<string>("B", "nav-b")], [])
            ]);
        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 10)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 20)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 30)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 40)));

        Assert.Equal(["nav-b"], keyboard.Text);
        Assert.Single(fallback.Events);
        Assert.Equal((ushort)'B', fallback.Events[0].Key.VirtualKey);
    }

    [Fact]
    public void MOD_presses_modifier_once_and_releases_it_on_source_key_up()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("MOD", ["Ctrl"]))])
            ]);
        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 5)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 10)));

        Assert.Equal(
            [
                new RecordedKey(WindowsKeyMap.Keyboard(WindowsKeyMap.Control), KeyEventKind.Down),
                new RecordedKey(WindowsKeyMap.Keyboard(WindowsKeyMap.Control), KeyEventKind.Up)
            ],
            keyboard.Keys);
        Assert.Empty(fallback.Events);
    }

    private static KeyboardEvent Physical(ushort virtualKey, KeyEventKind kind, long timestampMs)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestampMs);

    private readonly record struct RecordedKey(KeyboardKey Key, KeyEventKind Kind);

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<RecordedKey> Keys { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind) => Keys.Add(new RecordedKey(key, kind));

        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }

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
