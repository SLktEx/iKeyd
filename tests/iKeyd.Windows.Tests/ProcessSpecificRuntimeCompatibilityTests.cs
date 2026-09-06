using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ProcessSpecificRuntimeCompatibilityTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Theory]
    [InlineData('V', 'P')]
    [InlineData('X', 'K')]
    public void Console_ctrl_hotkeys_emit_legacy_system_menu_sequence(char input, char finalKey)
    {
        var state = new KeyboardState();
        var keyboard = new RecordingKeyboardOutput();
        var desktop = new ContextDesktopBackend("ConsoleWindowClass");
        using var runtime = CreateRuntime(state, keyboard, desktop);

        ApplyStateOnly(state, WindowsKeyMap.Control, KeyEventKind.Down);
        var disposition = Dispatch(runtime, state, input, KeyEventKind.Down, 10);

        Assert.Equal(KeyboardDisposition.Suppress, disposition);
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == WindowsKeyMap.Space && item.Kind == KeyEventKind.Down);
        Assert.Equal($"e{char.ToLowerInvariant(finalKey)}", keyboard.Text);
    }

    [Fact]
    public void Console_ctrl_v_does_not_fire_with_extra_shift()
    {
        var state = new KeyboardState();
        var keyboard = new RecordingKeyboardOutput();
        var desktop = new ContextDesktopBackend("ConsoleWindowClass");
        using var runtime = CreateRuntime(state, keyboard, desktop);

        ApplyStateOnly(state, WindowsKeyMap.Control, KeyEventKind.Down);
        ApplyStateOnly(state, WindowsKeyMap.Shift, KeyEventKind.Down);
        var disposition = Dispatch(runtime, state, (ushort)'V', KeyEventKind.Down, 10);

        Assert.NotEqual(KeyboardDisposition.Suppress, disposition);
        Assert.Empty(keyboard.Events);
    }

    [Fact]
    public void Gsview_alt_e_is_consumed_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var state = new KeyboardState();
        var keyboard = new RecordingKeyboardOutput();
        var desktop = new ContextDesktopBackend("gsview_class");
        using var runtime = CreateRuntime(state, keyboard, desktop);

        ApplyStateOnly(state, WindowsKeyMap.Alt, KeyEventKind.Down);
        var disposition = Dispatch(runtime, state, (ushort)'E', KeyEventKind.Down, 10);

        Assert.Equal(KeyboardDisposition.Suppress, disposition);
        Assert.Empty(keyboard.Events);
    }

    private static IKeydRuntimeHandler CreateRuntime(
        KeyboardState state,
        RecordingKeyboardOutput keyboard,
        IDesktopBackend desktop)
    {
        var configuration = IKeydConfiguration.Load(ProfilePath);
        return new IKeydRuntimeHandler(
            configuration,
            new FixedInputMethod(),
            state,
            new LegacySendOutput(keyboard, desktop),
            desktop);
    }

    private static void ApplyStateOnly(KeyboardState state, ushort virtualKey, KeyEventKind kind)
        => state.Apply(new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, 0));

    private static KeyboardDisposition Dispatch(
        IKeydRuntimeHandler runtime,
        KeyboardState state,
        ushort virtualKey,
        KeyEventKind kind,
        long timestamp)
    {
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey), kind, KeyEventOrigin.Physical, timestamp);
        state.Apply(keyboardEvent);
        return runtime.OnKeyboardEvent(keyboardEvent);
    }

    private sealed class FixedInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => false;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<KeyboardEvent> Events { get; } = [];
        public string Text { get; private set; } = string.Empty;
        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new KeyboardEvent(key, kind, KeyEventOrigin.OwnInjected, 0));
        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }
        public void SendText(string text) => Text += text;
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class ContextDesktopBackend(string className) : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(100, 100, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => className;
        public bool IsWindow(WindowHandle window) => window == _window;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [_window];
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
