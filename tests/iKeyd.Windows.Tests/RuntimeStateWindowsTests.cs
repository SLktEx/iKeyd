using iKeyd.App;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using iKeyd.Core.State;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class RuntimeStateWindowsTests
{
    [Fact]
    public void Router_applies_shared_state_actions_conditions_repeat_and_reset()
    {
        var stateProfile = new RuntimeStateProfile([
            RuntimeStateFieldProfile.String("mode", "normal"),
            RuntimeStateFieldProfile.Bool("armed", false)
        ]);
        var state = new RuntimeStateStore(stateProfile);
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
                        new BehaviorMappingProfile("A", Set("mode", "coding")),
                        new BehaviorMappingProfile("B", StateWhen("mode", "coding", "Left", "Right")),
                        new BehaviorMappingProfile("C", Toggle("armed")),
                        new BehaviorMappingProfile("D", StateWhen("armed", "true", "Up", "Down"))
                    ]),
                new AutomationKeymapProfile("K", [], [])
            ],
            state: stateProfile);

        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            fallback,
            runtimeState: state);

        Press(router, 'A', 0);
        Assert.True(state.TryGetScalar("mode", out var mode));
        Assert.Equal("coding", mode);

        Press(router, 'B', 10);
        Assert.Equal(WindowsKeyMap.Left, Assert.Single(keyboard.Presses).VirtualKey);
        keyboard.Presses.Clear();

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 20)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('C', KeyEventKind.Down, 21)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('C', KeyEventKind.Up, 22)));
        Assert.True(state.TryGetScalar("armed", out var armed));
        Assert.Equal("true", armed);

        Press(router, 'D', 30);
        Assert.Equal(WindowsKeyMap.Up, Assert.Single(keyboard.Presses).VirtualKey);
        keyboard.Presses.Clear();

        router.ResetInputState();
        Assert.True(state.TryGetScalar("mode", out var resetMode));
        Assert.Equal("normal", resetMode);
        Assert.True(state.TryGetScalar("armed", out var resetArmed));
        Assert.Equal("false", resetArmed);

        Press(router, 'B', 40);
        Press(router, 'D', 50);
        Assert.Equal(
            [WindowsKeyMap.Right, WindowsKeyMap.Down],
            keyboard.Presses.Select(key => key.VirtualKey).ToArray());
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void State_only_profile_requires_no_system_query_snapshot()
    {
        var stateProfile = new RuntimeStateProfile([RuntimeStateFieldProfile.Bool("flag", true)]);
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [],
                    [],
                    [new BehaviorMappingProfile("A", StateWhen("flag", "true", "Escape", "F1"))]),
                new AutomationKeymapProfile("K", [], [])
            ],
            state: stateProfile);

        Assert.Empty(profile.SystemQueries);

        var keyboard = new RecordingKeyboardOutput();
        using var router = new BehaviorWindowsInputRouter(
            profile,
            () => "S",
            new LegacySendOutput(keyboard),
            keyboard,
            new RecordingHandler());

        Press(router, 'A', 0);
        Assert.Equal(WindowsKeyMap.Escape, Assert.Single(keyboard.Presses).VirtualKey);
    }

    private static BehaviorInvocationProfile Set(string field, string value)
        => new(
            "SET",
            [],
            new Dictionary<string, string>
            {
                ["state"] = field,
                ["value"] = value
            });

    private static BehaviorInvocationProfile Toggle(string field)
        => new(
            "TOGGLE",
            [],
            new Dictionary<string, string> { ["state"] = field });

    private static BehaviorInvocationProfile StateWhen(
        string field,
        string expected,
        string thenKey,
        string elseKey)
        => new(
            "WHEN",
            [],
            new Dictionary<string, string>
            {
                ["state"] = field,
                ["operator"] = "equals",
                ["expected"] = expected,
                ["then_kind"] = "key",
                ["then_value"] = thenKey,
                ["else_kind"] = "key",
                ["else_value"] = elseKey
            });

    private static void Press(BehaviorWindowsInputRouter router, ushort virtualKey, long timestamp)
    {
        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical(virtualKey, KeyEventKind.Down, timestamp)));
        Assert.Equal(
            KeyboardDisposition.Suppress,
            router.OnKeyboardEvent(Physical(virtualKey, KeyEventKind.Up, timestamp + 1)));
    }

    private static KeyboardEvent Physical(ushort virtualKey, KeyEventKind kind, long timestampMs)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestampMs);

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<KeyboardKey> Presses { get; } = [];
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) => Presses.Add(key);
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class RecordingHandler : IKeyboardEventHandler, IInputStateResettable
    {
        public List<KeyboardEvent> Events { get; } = [];
        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            Events.Add(keyboardEvent);
            return KeyboardDisposition.PassThrough;
        }
        public void ResetInputState() { }
    }
}
