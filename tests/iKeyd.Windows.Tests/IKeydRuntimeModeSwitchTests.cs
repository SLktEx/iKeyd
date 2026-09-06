using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Macros;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class IKeydRuntimeModeSwitchTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Theory]
    [InlineData('1', InputMode.S, KeymapMode.S)]
    [InlineData('2', InputMode.R, null)]
    [InlineData('3', InputMode.T, KeymapMode.S)]
    [InlineData('4', InputMode.K, KeymapMode.K)]
    public void Physical_M_digit_matches_legacy_process_mode_switch(
        char digit,
        InputMode expectedMode,
        KeymapMode? expectedKeymap)
    {
        using var fixture = CreateRuntime(InputMode.S);

        Dispatch(fixture.Runtime, fixture.KeyboardState, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture.Runtime, fixture.KeyboardState, digit, KeyEventKind.Down, 10);
        Dispatch(fixture.Runtime, fixture.KeyboardState, digit, KeyEventKind.Up, 11);
        Dispatch(fixture.Runtime, fixture.KeyboardState, WindowsKeyMap.NonConvert, KeyEventKind.Up, 20);

        Assert.Equal(expectedMode, fixture.Runtime.Mode.Mode);
        Assert.Equal(expectedKeymap, fixture.Runtime.Mode.ActiveKeymap);
    }

    [Fact]
    public void Process3_T_preserves_the_previously_selected_K_keymap()
    {
        using var fixture = CreateRuntime(InputMode.K);

        Dispatch(fixture.Runtime, fixture.KeyboardState, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture.Runtime, fixture.KeyboardState, '3', KeyEventKind.Down, 10);
        Dispatch(fixture.Runtime, fixture.KeyboardState, '3', KeyEventKind.Up, 11);
        Dispatch(fixture.Runtime, fixture.KeyboardState, WindowsKeyMap.NonConvert, KeyEventKind.Up, 20);

        Assert.Equal(InputMode.T, fixture.Runtime.Mode.Mode);
        Assert.Equal(KeymapMode.K, fixture.Runtime.Mode.ActiveKeymap);
    }

    [Fact]
    public async Task Macro_hotkey_dispatch_uses_the_same_process4_mode_switch()
    {
        using var fixture = CreateRuntime(InputMode.S);

        await fixture.Runtime.DispatchAsync(new MacroHotkey("M", '4'), CancellationToken.None);

        Assert.Equal(InputMode.K, fixture.Runtime.Mode.Mode);
        Assert.Equal(KeymapMode.K, fixture.Runtime.Mode.ActiveKeymap);
    }

    private static RuntimeFixture CreateRuntime(InputMode startupMode)
    {
        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = startupMode };
        var keyboardState = new KeyboardState();
        var output = new NullKeyboardOutput();
        var runtime = new IKeydRuntimeHandler(
            configuration,
            new InactiveInputMethod(),
            keyboardState,
            new LegacySendOutput(output),
            new NullDesktopBackend());
        return new RuntimeFixture(runtime, keyboardState);
    }

    private static void Dispatch(
        IKeydRuntimeHandler runtime,
        KeyboardState keyboardState,
        ushort virtualKey,
        KeyEventKind kind,
        long timestampMs)
    {
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey),
            kind,
            KeyEventOrigin.Physical,
            timestampMs);
        keyboardState.Apply(keyboardEvent);
        runtime.OnKeyboardEvent(keyboardEvent);
    }

    private sealed record RuntimeFixture(IKeydRuntimeHandler Runtime, KeyboardState KeyboardState) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class InactiveInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => false;
    }

    private sealed class NullKeyboardOutput : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "ModeTest";
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
