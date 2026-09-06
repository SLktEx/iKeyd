using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ConfiguredBehaviorKeyboardHandlerTests
{
    [Fact]
    public void Mod_tap_interrupt_modifies_next_physical_key()
    {
        var output = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(output);
        var fallback = new RecordingHandler();
        var profile = new KeyBehaviorProfile([
            new KeyBehaviorBinding(
                KeyCode.A,
                KeyBehaviorAction.Key("A"),
                KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control),
                180,
                TapHoldInterruptPolicy.Hold)
        ]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, send, fallback);

        Assert.Equal(KeyboardDisposition.Suppress, handler.OnKeyboardEvent(Event('A', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, handler.OnKeyboardEvent(Event('Q', KeyEventKind.Down, 50)));
        Assert.Equal(KeyboardDisposition.Suppress, handler.OnKeyboardEvent(Event('Q', KeyEventKind.Up, 60)));
        Assert.Equal(KeyboardDisposition.Suppress, handler.OnKeyboardEvent(Event('A', KeyEventKind.Up, 70)));

        Assert.Empty(fallback.Events);
        Assert.Equal(
            [
                new Observed(WindowsKeyMap.Control, KeyEventKind.Down),
                new Observed((ushort)'Q', KeyEventKind.Down),
                new Observed((ushort)'Q', KeyEventKind.Up),
                new Observed(WindowsKeyMap.Control, KeyEventKind.Up),
            ],
            output.Events);
    }

    [Fact]
    public void Layer_tap_maps_key_while_held()
    {
        var output = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(output);
        var fallback = new RecordingHandler();
        var profile = new KeyBehaviorProfile(
            [new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("NAV"))],
            [new KeyBehaviorLayer("NAV", [new KeyBehaviorLayerBinding(KeyCode.H, KeyBehaviorAction.Key("Left"))])]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, send, fallback);

        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Down, KeyEventOrigin.Physical, 0));
        var hDown = handler.OnKeyboardEvent(Event('H', KeyEventKind.Down, 50));
        var hUp = handler.OnKeyboardEvent(Event('H', KeyEventKind.Up, 60));
        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Up, KeyEventOrigin.Physical, 70));

        Assert.Equal(KeyboardDisposition.Suppress, hDown);
        Assert.Equal(KeyboardDisposition.Suppress, hUp);
        Assert.Empty(fallback.Events);
        Assert.Equal(
            [
                new Observed(WindowsKeyMap.Left, KeyEventKind.Down),
                new Observed(WindowsKeyMap.Left, KeyEventKind.Up),
            ],
            output.Events);
    }

    [Fact]
    public void Quick_layer_tap_emits_tap_key()
    {
        var output = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(output);
        var fallback = new RecordingHandler();
        var profile = new KeyBehaviorProfile(
            [new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("NAV"))],
            [new KeyBehaviorLayer("NAV", [])]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, send, fallback);

        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Down, KeyEventOrigin.Physical, 0));
        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Up, KeyEventOrigin.Physical, 100));

        Assert.Empty(fallback.Events);
        Assert.Equal(
            [
                new Observed(WindowsKeyMap.Space, KeyEventKind.Down),
                new Observed(WindowsKeyMap.Space, KeyEventKind.Up),
            ],
            output.Events);
    }

    [Fact]
    public void Empty_behavior_profile_is_exact_fallback()
    {
        var output = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var handler = new ConfiguredBehaviorKeyboardHandler(KeyBehaviorProfile.Empty, new LegacySendOutput(output), fallback);
        var input = Event('Q', KeyEventKind.Down, 1);

        var disposition = handler.OnKeyboardEvent(input);

        Assert.Equal(KeyboardDisposition.PassThrough, disposition);
        Assert.Equal([input], fallback.Events);
        Assert.Empty(output.Events);
    }

    private static KeyboardEvent Event(char key, KeyEventKind kind, long timestamp)
        => new(WindowsKeyMap.Keyboard(key), kind, KeyEventOrigin.Physical, timestamp);

    private sealed class RecordingHandler : IKeyboardEventHandler
    {
        public List<KeyboardEvent> Events { get; } = [];
        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            Events.Add(keyboardEvent);
            return KeyboardDisposition.PassThrough;
        }
    }

    private readonly record struct Observed(ushort VirtualKey, KeyEventKind Kind);

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<Observed> Events { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new Observed(key.VirtualKey, kind));

        public void SendKeyPress(KeyboardKey key)
        {
            Events.Add(new Observed(key.VirtualKey, KeyEventKind.Down));
            Events.Add(new Observed(key.VirtualKey, KeyEventKind.Up));
        }

        public void SendText(string text) => throw new Xunit.Sdk.XunitException($"Unexpected text output: {text}");
        public bool IsToggleOn(ushort virtualKey) => false;
    }
}
