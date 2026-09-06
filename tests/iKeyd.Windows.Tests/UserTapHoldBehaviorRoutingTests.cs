using iKeyd.App;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class UserTapHoldBehaviorRoutingTests
{
    [Fact]
    public void Quick_release_routes_custom_on_tap_to_windows_key_output()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(keyboard, fallback, holdOnOtherKeyPress: true);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 50)));

        var press = Assert.Single(keyboard.KeyPresses);
        Assert.Equal((ushort)'X', press.VirtualKey);
        Assert.Empty(keyboard.Text);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Interrupt_resolves_custom_hold_before_interrupting_key_layer_lookup()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(keyboard, fallback, holdOnOtherKeyPress: true);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 21)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 30)));

        Assert.Equal(["num-b"], keyboard.Text);
        Assert.Empty(keyboard.KeyPresses);
        Assert.Empty(fallback.Events);

        // The hold-owned NUM layer must be released with A. A later B therefore
        // reaches the ordinary fallback instead of remaining stuck on NUM.
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 40)));
        Assert.Single(fallback.Events);
    }

    [Fact]
    public void Hold_on_other_key_press_false_does_not_activate_layer_before_timeout()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = Router(keyboard, fallback, holdOnOtherKeyPress: false);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 21)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 30)));

        var tap = Assert.Single(keyboard.KeyPresses);
        Assert.Equal((ushort)'X', tap.VirtualKey);
        Assert.Empty(keyboard.Text);
        Assert.Equal(2, fallback.Events.Count);
    }

    private static BehaviorWindowsInputRouter Router(
        RecordingKeyboardOutput keyboard,
        RecordingHandler fallback,
        bool holdOnOtherKeyPress)
    {
        var definition = new UserBehaviorDefinitionProfile(
            "SMART_TH",
            ["tap_key", "layer_name"],
            handlers:
            [
                new UserBehaviorHandlerProfile(
                    "hold",
                    [],
                    [new UserBehaviorStatementProfile("layer_on", value: "layer_name")]),
                new UserBehaviorHandlerProfile(
                    "tap",
                    [],
                    [new UserBehaviorStatementProfile("send", value: "tap_key")])
            ]);

        var invocation = new BehaviorInvocationProfile(
            "SMART_TH",
            ["X", "NUM"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tapping_term"] = "200ms",
                ["hold_on_other_key_press"] = holdOnOtherKeyPress.ToString().ToLowerInvariant()
            });

        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", invocation)]),
                new AutomationKeymapProfile("K", [], []),
                new AutomationKeymapProfile(
                    "NUM",
                    [new SingleMapping<string>("B", "num-b")],
                    [])
            ],
            behaviorDefinitions: [definition]);

        return new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback);
    }

    private static KeyboardEvent Physical(ushort virtualKey, KeyEventKind kind, long timestampMs)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestampMs);

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<KeyboardKey> KeyPresses { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) => KeyPresses.Add(key);
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
