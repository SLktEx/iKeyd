using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class UnicodeTextBehaviorWindowsTests
{
    [Fact]
    public void Unicode_mapping_repeats_but_text_mapping_does_not()
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
                    [
                        new BehaviorMappingProfile(
                            "A",
                            new BehaviorInvocationProfile(
                                "UNICODE",
                                [],
                                new Dictionary<string, string> { ["value"] = "🦀" })),
                        new BehaviorMappingProfile(
                            "B",
                            new BehaviorInvocationProfile(
                                "TEXT",
                                [],
                                new Dictionary<string, string> { ["value"] = "hello 世界" }))
                    ]),
                new AutomationKeymapProfile("K", [], [])
            ]);
        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 30)));

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 40)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 60)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 70)));

        Assert.Equal(["🦀", "🦀", "hello 世界"], keyboard.Text);
        Assert.Empty(keyboard.Keys);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Stateful_behavior_repeat_is_still_not_replayed_through_windows_router()
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
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("MOD", ["Ctrl"]))]),
                new AutomationKeymapProfile("K", [], [])
            ]);
        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0));
        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 20));
        router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 30));

        Assert.Equal(2, keyboard.Keys.Count);
        Assert.Equal(KeyEventKind.Down, keyboard.Keys[0].Kind);
        Assert.Equal(KeyEventKind.Up, keyboard.Keys[1].Kind);
        Assert.Equal(WindowsKeyMap.Control, keyboard.Keys[0].Key.VirtualKey);
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
