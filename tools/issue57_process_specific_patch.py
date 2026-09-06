from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

runtime = "src/iKeyd.App/IKeydRuntimeHandler.cs"
replace_once(runtime,
    "using iKeyd.Core.Chords;",
    "using System.Runtime.InteropServices;\nusing iKeyd.Core.Chords;")
replace_once(runtime,
    "internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable\n{",
    "internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable\n{\n    private const uint WmCommand = 0x0111;")
replace_once(runtime,
    "    private long _timerDueAt;\n    private bool _disposed;",
    "    private long _timerDueAt;\n    private bool _suspended;\n    private bool _disposed;")
replace_once(runtime,
    '''    public void SetMode(InputMode mode)\n    {''',
    '''    internal bool IsSuspended\n    {\n        get\n        {\n            lock (_gate)\n                return _suspended;\n        }\n    }\n\n    public void SetMode(InputMode mode)\n    {''')
replace_once(runtime,
    '''            if (_disposed)\n                return KeyboardDisposition.PassThrough;\n\n            if (TryHandleLayerKey(keyboardEvent))''',
    '''            if (_disposed)\n                return KeyboardDisposition.PassThrough;\n\n            if (TryHandleSuspendToggle(keyboardEvent))\n                return KeyboardDisposition.Suppress;\n\n            if (_suspended)\n            {\n                if (keyboardEvent.Kind == KeyEventKind.Up &&\n                    _suppressedKeys.Remove(keyboardEvent.Key.VirtualKey))\n                    return KeyboardDisposition.Suppress;\n                return KeyboardDisposition.PassThrough;\n            }\n\n            if (TryHandleContextHotkey(keyboardEvent))\n                return KeyboardDisposition.Suppress;\n\n            if (TryHandleLayerKey(keyboardEvent))''')
replace_once(runtime,
    '''    private bool TryHandleLayerKey(KeyboardEvent keyboardEvent)\n    {''',
    '''    private bool TryHandleSuspendToggle(KeyboardEvent keyboardEvent)\n    {\n        if (keyboardEvent.Key.VirtualKey != WindowsKeyMap.Escape ||\n            keyboardEvent.Kind != KeyEventKind.Down ||\n            !_keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control) ||\n            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt) ||\n            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Shift) ||\n            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.LeftWin) ||\n            _keyboardState.IsVirtualKeyPressed(0x5C))\n            return false;\n\n        FlushAllPending();\n        _suspended = !_suspended;\n        _suppressedKeys.Add(WindowsKeyMap.Escape);\n        return true;\n    }\n\n    private bool TryHandleContextHotkey(KeyboardEvent keyboardEvent)\n    {\n        if (keyboardEvent.Kind != KeyEventKind.Down)\n            return false;\n\n        var window = _desktop.GetActiveWindow();\n        if (!_desktop.IsWindow(window))\n            return false;\n\n        var className = _desktop.GetWindowClass(window);\n        if (string.IsNullOrEmpty(className))\n            return false;\n\n        var ctrl = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control);\n        var alt = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt);\n        var shift = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Shift);\n        var win = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.LeftWin) ||\n                  _keyboardState.IsVirtualKeyPressed(0x5C);\n\n        if (string.Equals(className, "ConsoleWindowClass", StringComparison.Ordinal) &&\n            ctrl && !alt && !shift && !win)\n        {\n            if (keyboardEvent.Key.VirtualKey == (ushort)'V')\n            {\n                FlushAllPending();\n                _send.Send("!{Space}ep");\n                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n                return true;\n            }\n\n            if (keyboardEvent.Key.VirtualKey == (ushort)'X')\n            {\n                FlushAllPending();\n                _send.Send("!{Space}ek");\n                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n                return true;\n            }\n        }\n\n        if (string.Equals(className, "gsview_class", StringComparison.Ordinal) &&\n            alt && !ctrl && !shift && !win &&\n            keyboardEvent.Key.VirtualKey == (ushort)'E')\n        {\n            FlushAllPending();\n            NativeMethods.PostMessageW(window.Value, WmCommand, 105, 0);\n            _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n            return true;\n        }\n\n        return false;\n    }\n\n    private bool TryHandleLayerKey(KeyboardEvent keyboardEvent)\n    {''')
replace_once(runtime,
    '''    private void ThrowIfDisposed()\n        => ObjectDisposedException.ThrowIf(_disposed, this);\n}''',
    '''    private void ThrowIfDisposed()\n        => ObjectDisposedException.ThrowIf(_disposed, this);\n\n    private static class NativeMethods\n    {\n        [DllImport("user32.dll", SetLastError = true)]\n        [return: MarshalAs(UnmanagedType.Bool)]\n        public static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);\n    }\n}''')

Path("tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs").write_text(r'''using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ProcessSpecificRuntimeCompatibilityTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    public void Ctrl_Escape_toggles_suspend_and_suspended_runtime_passes_normal_keys()
    {
        var state = new KeyboardState();
        var keyboard = new RecordingKeyboardOutput();
        var desktop = new ContextDesktopBackend("iKeydScenarioWindow");
        using var runtime = CreateRuntime(state, keyboard, desktop);

        ApplyStateOnly(state, WindowsKeyMap.Control, KeyEventKind.Down);
        var disposition = Dispatch(runtime, state, WindowsKeyMap.Escape, KeyEventKind.Down, 10);

        Assert.Equal(KeyboardDisposition.Suppress, disposition);
        Assert.True(runtime.IsSuspended);
        Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(runtime, state, (ushort)'Q', KeyEventKind.Down, 20));

        Dispatch(runtime, state, WindowsKeyMap.Escape, KeyEventKind.Up, 30);
        var resumed = Dispatch(runtime, state, WindowsKeyMap.Escape, KeyEventKind.Down, 40);
        Assert.Equal(KeyboardDisposition.Suppress, resumed);
        Assert.False(runtime.IsSuspended);
    }

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
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)'E' && item.Kind == KeyEventKind.Down);
        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)finalKey && item.Kind == KeyEventKind.Down);
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
        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new KeyboardEvent(key, kind, KeyEventOrigin.OwnInjected, 0));
        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }
        public void SendText(string text) { }
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
''', encoding="utf-8")
