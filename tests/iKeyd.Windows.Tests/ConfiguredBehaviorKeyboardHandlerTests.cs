using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
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
        var desktop = new RecordingDesktopBackend();
        var fallback = new RecordingHandler();
        var profile = new KeyBehaviorProfile([
            new KeyBehaviorBinding(
                KeyCode.A,
                KeyBehaviorAction.Key("A"),
                KeyBehaviorAction.Modifier(KeyBehaviorModifier.Control),
                180,
                TapHoldInterruptPolicy.Hold)
        ]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, send, desktop, fallback);

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
        var desktop = new RecordingDesktopBackend();
        var fallback = new RecordingHandler();
        var profile = new KeyBehaviorProfile(
            [new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("NAV"))],
            [new KeyBehaviorLayer("NAV", [new KeyBehaviorLayerBinding(KeyCode.H, KeyBehaviorAction.Key("Left"))])]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, send, desktop, fallback);

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
        var desktop = new RecordingDesktopBackend();
        var fallback = new RecordingHandler();
        var profile = new KeyBehaviorProfile(
            [new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("NAV"))],
            [new KeyBehaviorLayer("NAV", [])]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, send, desktop, fallback);

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
    public void Configured_layer_dispatches_mouse_media_and_window_actions()
    {
        var output = new RecordingKeyboardOutput();
        var send = new LegacySendOutput(output);
        var desktop = new RecordingDesktopBackend();
        var fallback = new RecordingHandler();
        var profile = new KeyBehaviorProfile(
            [new KeyBehaviorBinding(KeyCode.Space, KeyBehaviorAction.Key("Space"), KeyBehaviorAction.Layer("DESKTOP"))],
            [new KeyBehaviorLayer("DESKTOP",
            [
                new KeyBehaviorLayerBinding(KeyCode.H, KeyBehaviorAction.MouseMove(-30, 10)),
                new KeyBehaviorLayerBinding(KeyCode.J, KeyBehaviorAction.MouseClick("Left")),
                new KeyBehaviorLayerBinding(KeyCode.K, KeyBehaviorAction.Scroll("Down")),
                new KeyBehaviorLayerBinding(KeyCode.L, KeyBehaviorAction.Media("PlayPause")),
                new KeyBehaviorLayerBinding(KeyCode.U, KeyBehaviorAction.Window("LeftHalf")),
            ])]);
        var handler = new ConfiguredBehaviorKeyboardHandler(profile, send, desktop, fallback);

        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Down, KeyEventOrigin.Physical, 0));
        Press(handler, 'H', 20);
        Press(handler, 'J', 30);
        Press(handler, 'K', 40);
        Press(handler, 'L', 50);
        Press(handler, 'U', 60);
        handler.OnKeyboardEvent(new KeyboardEvent(WindowsKeyMap.Keyboard(WindowsKeyMap.Space), KeyEventKind.Up, KeyEventOrigin.Physical, 70));

        Assert.Empty(fallback.Events);
        Assert.Empty(output.Events);
        Assert.Equal([(-30, 10)], desktop.Moves);
        Assert.Equal([DesktopMouseButton.Left], desktop.Clicks);
        Assert.Equal([-120], desktop.Scrolls);
        Assert.Equal([DesktopMediaCommand.PlayPause], desktop.Media);
        Assert.Equal(new DesktopRect(0, 0, 600, 800), desktop.Bounds);
    }

    [Fact]
    public void Empty_behavior_profile_is_exact_fallback()
    {
        var output = new RecordingKeyboardOutput();
        var desktop = new RecordingDesktopBackend();
        var fallback = new RecordingHandler();
        var handler = new ConfiguredBehaviorKeyboardHandler(KeyBehaviorProfile.Empty, new LegacySendOutput(output), desktop, fallback);
        var input = Event('Q', KeyEventKind.Down, 1);

        var disposition = handler.OnKeyboardEvent(input);

        Assert.Equal(KeyboardDisposition.PassThrough, disposition);
        Assert.Equal([input], fallback.Events);
        Assert.Empty(output.Events);
    }

    private static void Press(ConfiguredBehaviorKeyboardHandler handler, char key, long timestamp)
    {
        Assert.Equal(KeyboardDisposition.Suppress, handler.OnKeyboardEvent(Event(key, KeyEventKind.Down, timestamp)));
        Assert.Equal(KeyboardDisposition.Suppress, handler.OnKeyboardEvent(Event(key, KeyEventKind.Up, timestamp + 1)));
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

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        private static readonly WindowHandle Active = new(1);

        public List<(int X, int Y)> Moves { get; } = [];
        public List<DesktopMouseButton> Clicks { get; } = [];
        public List<int> Scrolls { get; } = [];
        public List<DesktopMediaCommand> Media { get; } = [];
        public DesktopRect Bounds { get; private set; } = new(100, 100, 800, 600);
        public DesktopWindowState WindowState { get; private set; } = DesktopWindowState.Normal;
        public bool TopMost { get; private set; }
        public bool Caption { get; private set; } = true;
        public byte? Opacity { get; private set; }

        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => WindowState;
        public DesktopRect GetWindowBounds(WindowHandle window) => Bounds;
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1200, 800);
        public string? GetWindowClass(WindowHandle window) => "Test";
        public bool IsWindow(WindowHandle window) => !window.IsEmpty;
        public void Minimize(WindowHandle window) => WindowState = DesktopWindowState.Minimized;
        public void Maximize(WindowHandle window) => WindowState = DesktopWindowState.Maximized;
        public void Restore(WindowHandle window) => WindowState = DesktopWindowState.Normal;
        public void MoveResize(WindowHandle window, DesktopRect bounds) => Bounds = bounds;
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [Active];
        public bool IsTopMost(WindowHandle window) => TopMost;
        public void SetTopMost(WindowHandle window, bool enabled) => TopMost = enabled;
        public byte? GetOpacity(WindowHandle window) => Opacity;
        public void SetOpacity(WindowHandle window, byte? opacity) => Opacity = opacity;
        public bool HasCaption(WindowHandle window) => Caption;
        public void SetCaption(WindowHandle window, bool enabled) => Caption = enabled;
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) => Moves.Add((deltaX, deltaY));
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) => Clicks.Add(button);
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) => Scrolls.Add(wheelDelta);
        public void SendMediaCommand(DesktopMediaCommand command) => Media.Add(command);
    }
}
