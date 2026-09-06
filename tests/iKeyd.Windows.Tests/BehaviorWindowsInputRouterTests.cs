using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class BehaviorWindowsInputRouterTests
{
    [Fact]
    public void LT_tap_sends_tap_key_and_suppresses_source_key()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = CreateRouter(keyboard, fallback);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 100)));

        Assert.Equal(
            [new RecordedKey(WindowsKeyMap.Keyboard('Z'), KeyEventKind.Down),
             new RecordedKey(WindowsKeyMap.Keyboard('Z'), KeyEventKind.Up)],
            keyboard.Keys);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void LT_interrupt_activates_named_layer_before_interrupting_key_is_mapped()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = CreateRouter(keyboard, fallback);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0));
        var bDown = router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 50));
        var bUp = router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 60));
        router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 70));

        Assert.Equal(KeyboardDisposition.Suppress, bDown);
        Assert.Equal(KeyboardDisposition.Suppress, bUp);
        Assert.Equal(["num-b"], keyboard.Text);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void LT_can_be_bound_to_space_and_unmapped_layer_keys_remain_transparent()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = CreateRouter(keyboard, fallback, sourceKey: "Space", tapKey: "Enter");

        router.OnKeyboardEvent(Physical(WindowsKeyMap.Space, KeyEventKind.Down, 0));
        router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 50));

        Assert.Single(fallback.Events);
        Assert.Equal((ushort)'C', fallback.Events[0].Key.VirtualKey);

        router.OnKeyboardEvent(Physical('C', KeyEventKind.Up, 60));
        router.OnKeyboardEvent(Physical(WindowsKeyMap.Space, KeyEventKind.Up, 70));

        Assert.DoesNotContain(keyboard.Keys, item => item.Key.VirtualKey == WindowsKeyMap.Enter);
    }

    [Fact]
    public void MT_interrupt_presses_modifier_while_interrupting_key_remains_transparent()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = CreateMtRouter(keyboard, fallback);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 50)));
        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 60)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 70)));

        Assert.Equal(
            [new RecordedKey(WindowsKeyMap.Keyboard(WindowsKeyMap.Control), KeyEventKind.Down),
             new RecordedKey(WindowsKeyMap.Keyboard(WindowsKeyMap.Control), KeyEventKind.Up)],
            keyboard.Keys);
        Assert.Equal(2, fallback.Events.Count);
        Assert.All(fallback.Events, item => Assert.Equal((ushort)'B', item.Key.VirtualKey));
    }

    [Fact]
    public void Custom_behavior_local_state_can_activate_layer_before_interrupting_key()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = CreateCustomRouter(keyboard, fallback);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0));
        var bDown = router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 30));
        var bUp = router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 40));
        router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 50));

        Assert.Equal(KeyboardDisposition.Suppress, bDown);
        Assert.Equal(KeyboardDisposition.Suppress, bUp);
        Assert.Equal(["custom-num-b"], keyboard.Text);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Custom_behavior_tap_path_sends_bound_parameter_key()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = CreateCustomRouter(keyboard, fallback);

        router.OnKeyboardEvent(Physical('A', KeyEventKind.Down, 0));
        router.OnKeyboardEvent(Physical('A', KeyEventKind.Up, 20));

        Assert.Equal(
            [new RecordedKey(WindowsKeyMap.Keyboard('X'), KeyEventKind.Down),
             new RecordedKey(WindowsKeyMap.Keyboard('X'), KeyEventKind.Up)],
            keyboard.Keys);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void Injected_events_bypass_behavior_routing()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        using var router = CreateRouter(keyboard, fallback);

        var injected = new KeyboardEvent(
            WindowsKeyMap.Keyboard('A'),
            KeyEventKind.Down,
            KeyEventOrigin.OwnInjected,
            0);

        Assert.Equal(KeyboardDisposition.PassThrough, router.OnKeyboardEvent(injected));
        Assert.Single(fallback.Events);
        Assert.Empty(keyboard.Keys);
    }

    private static BehaviorWindowsInputRouter CreateRouter(
        RecordingKeyboardOutput keyboard,
        RecordingHandler fallback,
        string sourceKey = "A",
        string tapKey = "Z")
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile(sourceKey, new BehaviorInvocationProfile("LT", ["NUM", tapKey]))]),
                new AutomationKeymapProfile("K", [], []),
                new AutomationKeymapProfile("NUM", [new SingleMapping<string>("B", "num-b")], [])
            ]);
        return new BehaviorWindowsInputRouter(profile, () => "S", new LegacySendOutput(keyboard), keyboard, fallback);
    }

    private static BehaviorWindowsInputRouter CreateMtRouter(
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
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("MT", ["Ctrl", "X"]))]),
                new AutomationKeymapProfile("K", [], [])
            ]);
        return new BehaviorWindowsInputRouter(profile, () => "S", new LegacySendOutput(keyboard), keyboard, fallback);
    }

    private static BehaviorWindowsInputRouter CreateCustomRouter(
        RecordingKeyboardOutput keyboard,
        RecordingHandler fallback)
    {
        var definition = new UserBehaviorDefinitionProfile(
            "SMART_LT",
            ["tap_key", "layer_name"],
            [new UserBehaviorLocalProfile("interrupted")],
            [
                new UserBehaviorHandlerProfile(
                    "interrupt",
                    ["other"],
                    [
                        new UserBehaviorStatementProfile("set_bool", target: "interrupted", value: "true"),
                        new UserBehaviorStatementProfile("layer_on", value: "layer_name")
                    ]),
                new UserBehaviorHandlerProfile(
                    "release",
                    [],
                    [
                        new UserBehaviorStatementProfile(
                            "if_bool",
                            condition: "interrupted",
                            thenStatements: [new UserBehaviorStatementProfile("layer_off", value: "layer_name")],
                            elseStatements: [new UserBehaviorStatementProfile("send", value: "tap_key")])
                    ])
            ]);
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", new BehaviorInvocationProfile("SMART_LT", ["X", "NUM"]))]),
                new AutomationKeymapProfile("K", [], []),
                new AutomationKeymapProfile("NUM", [new SingleMapping<string>("B", "custom-num-b")], [])
            ],
            behaviorDefinitions: [definition]);
        return new BehaviorWindowsInputRouter(profile, () => "S", new LegacySendOutput(keyboard), keyboard, fallback);
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
