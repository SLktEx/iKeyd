using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyContextualHotkeyHandlerTests
{
    [Theory]
    [InlineData('V', "ep")]
    [InlineData('X', "ek")]
    public void Console_ctrl_hotkeys_release_control_send_system_menu_sequence_and_restore_it(
        char trigger,
        string expectedText)
    {
        var state = new KeyboardState();
        var desktop = new RecordingDesktopBackend("ConsoleWindowClass");
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var commands = new List<(WindowHandle Window, uint Command)>();
        var handler = new LegacyContextualHotkeyHandler(
            state,
            desktop,
            keyboard,
            new LegacySendOutput(keyboard, desktop),
            (window, command) => commands.Add((window, command)),
            fallback);

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(handler, state, WindowsKeyMap.Control, KeyEventKind.Down));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(handler, state, trigger, KeyEventKind.Down));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(handler, state, trigger, KeyEventKind.Up));

        Assert.Equal(expectedText, keyboard.Text);
        Assert.Equal(
            [
                (WindowsKeyMap.Control, KeyEventKind.Up),
                ((ushort)0xA4, KeyEventKind.Down),
                (WindowsKeyMap.Space, KeyEventKind.Down),
                (WindowsKeyMap.Space, KeyEventKind.Up),
                ((ushort)0xA4, KeyEventKind.Up),
                (WindowsKeyMap.Control, KeyEventKind.Down)
            ],
            keyboard.Events.Select(item => (item.Key.VirtualKey, item.Kind)).ToArray());
        Assert.Empty(commands);
        Assert.DoesNotContain(fallback.Events, item => item.Key.VirtualKey == trigger);
    }

    [Fact]
    public void Console_left_control_is_restored_as_the_same_physical_modifier()
    {
        const ushort leftControl = 0xA2;
        var state = new KeyboardState();
        var desktop = new RecordingDesktopBackend("ConsoleWindowClass");
        var keyboard = new RecordingKeyboardOutput();
        var handler = new LegacyContextualHotkeyHandler(
            state,
            desktop,
            keyboard,
            new LegacySendOutput(keyboard, desktop),
            (_, _) => { },
            new RecordingHandler());

        Dispatch(handler, state, leftControl, KeyEventKind.Down);
        Dispatch(handler, state, 'V', KeyEventKind.Down);

        Assert.Equal(leftControl, keyboard.Events[0].Key.VirtualKey);
        Assert.Equal(KeyEventKind.Up, keyboard.Events[0].Kind);
        Assert.Equal(leftControl, keyboard.Events[^1].Key.VirtualKey);
        Assert.Equal(KeyEventKind.Down, keyboard.Events[^1].Kind);
    }

    [Fact]
    public void Gsview_alt_e_posts_command_105_and_suppresses_e_down_and_up()
    {
        var state = new KeyboardState();
        var desktop = new RecordingDesktopBackend("gsview_class");
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var commands = new List<(WindowHandle Window, uint Command)>();
        var handler = new LegacyContextualHotkeyHandler(
            state,
            desktop,
            keyboard,
            new LegacySendOutput(keyboard, desktop),
            (window, command) => commands.Add((window, command)),
            fallback);

        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(handler, state, WindowsKeyMap.Alt, KeyEventKind.Down));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(handler, state, 'E', KeyEventKind.Down));
        Assert.Equal(KeyboardDisposition.Suppress, Dispatch(handler, state, 'E', KeyEventKind.Up));

        Assert.Equal([(desktop.Active, 105u)], commands);
        Assert.Empty(keyboard.Events);
        Assert.Equal(string.Empty, keyboard.Text);
        Assert.DoesNotContain(fallback.Events, item => item.Key.VirtualKey == 'E');
    }

    [Fact]
    public void Same_shortcuts_in_an_unrelated_window_delegate_unchanged()
    {
        var state = new KeyboardState();
        var desktop = new RecordingDesktopBackend("NotLegacyContext");
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var handler = new LegacyContextualHotkeyHandler(
            state,
            desktop,
            keyboard,
            new LegacySendOutput(keyboard, desktop),
            (_, _) => throw new Xunit.Sdk.XunitException("command must not be posted"),
            fallback);

        Dispatch(handler, state, WindowsKeyMap.Control, KeyEventKind.Down);
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(handler, state, 'V', KeyEventKind.Down));

        Assert.Contains(fallback.Events, item => item.Key.VirtualKey == 'V');
        Assert.Empty(keyboard.Events);
        Assert.Equal(string.Empty, keyboard.Text);
    }

    private static KeyboardDisposition Dispatch(
        LegacyContextualHotkeyHandler handler,
        KeyboardState state,
        ushort virtualKey,
        KeyEventKind kind)
    {
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey),
            kind,
            KeyEventOrigin.Physical,
            0);
        state.Apply(keyboardEvent);
        return handler.OnKeyboardEvent(keyboardEvent);
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

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, KeyEventKind Kind)> Events { get; } = [];
        public string Text { get; private set; } = string.Empty;

        public void SendKey(KeyboardKey key, KeyEventKind kind) => Events.Add((key, kind));
        public void SendKeyPress(KeyboardKey key)
        {
            Events.Add((key, KeyEventKind.Down));
            Events.Add((key, KeyEventKind.Up));
        }
        public void SendText(string text) => Text += text;
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class RecordingDesktopBackend(string windowClass) : IDesktopBackend
    {
        public WindowHandle Active { get; } = new(42);
        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => window == Active ? windowClass : null;
        public bool IsWindow(WindowHandle window) => window == Active;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [Active];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
