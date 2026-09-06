using iKeyd.App;
using iKeyd.Core.Clipboard;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class IKeydRuntimeClipboardTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    public void ProcessV_M_routes_to_clipboard_picker_and_paste()
    {
        using var fixture = CreateRuntime();

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture, 'V', KeyEventKind.Down, 10);
        Dispatch(fixture, 'V', KeyEventKind.Up, 11);

        Assert.Equal(1, fixture.Clipboard.ShowPickerAndPasteCalls);
        Assert.Equal(0, fixture.Clipboard.CaptureLatestCalls);
        Assert.Equal(0, fixture.Clipboard.PasteCapturedCalls);
    }

    [Fact]
    public void ProcessV_MH_routes_to_capture_latest()
    {
        using var fixture = CreateRuntime();

        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0);
        Dispatch(fixture, WindowsKeyMap.Convert, KeyEventKind.Down, 5);
        Dispatch(fixture, 'V', KeyEventKind.Down, 10);
        Dispatch(fixture, 'V', KeyEventKind.Up, 11);

        Assert.Equal(0, fixture.Clipboard.ShowPickerAndPasteCalls);
        Assert.Equal(1, fixture.Clipboard.CaptureLatestCalls);
        Assert.Equal(0, fixture.Clipboard.PasteCapturedCalls);
    }

    [Fact]
    public void ProcessV_HM_routes_to_paste_captured()
    {
        using var fixture = CreateRuntime();

        Dispatch(fixture, WindowsKeyMap.Convert, KeyEventKind.Down, 0);
        Dispatch(fixture, WindowsKeyMap.NonConvert, KeyEventKind.Down, 5);
        Dispatch(fixture, 'V', KeyEventKind.Down, 10);
        Dispatch(fixture, 'V', KeyEventKind.Up, 11);

        Assert.Equal(0, fixture.Clipboard.ShowPickerAndPasteCalls);
        Assert.Equal(0, fixture.Clipboard.CaptureLatestCalls);
        Assert.Equal(1, fixture.Clipboard.PasteCapturedCalls);
    }

    private static RuntimeFixture CreateRuntime()
    {
        var keyboardState = new KeyboardState();
        var clipboard = new RecordingClipboardActions();
        var runtime = new IKeydRuntimeHandler(
            IKeydConfiguration.Load(ProfilePath),
            new InactiveInputMethod(),
            keyboardState,
            new LegacySendOutput(new NullKeyboardOutput()),
            new NullDesktopBackend(),
            clipboard);
        return new RuntimeFixture(runtime, keyboardState, clipboard);
    }

    private static void Dispatch(
        RuntimeFixture fixture,
        ushort virtualKey,
        KeyEventKind kind,
        long timestampMs)
    {
        var keyboardEvent = new KeyboardEvent(
            WindowsKeyMap.Keyboard(virtualKey),
            kind,
            KeyEventOrigin.Physical,
            timestampMs);
        fixture.KeyboardState.Apply(keyboardEvent);
        fixture.Runtime.OnKeyboardEvent(keyboardEvent);
    }

    private sealed record RuntimeFixture(
        IKeydRuntimeHandler Runtime,
        KeyboardState KeyboardState,
        RecordingClipboardActions Clipboard) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class RecordingClipboardActions : IClipboardHistoryActions
    {
        public int ShowPickerAndPasteCalls { get; private set; }
        public int CaptureLatestCalls { get; private set; }
        public int PasteCapturedCalls { get; private set; }

        public bool ShowPickerAndPaste()
        {
            ShowPickerAndPasteCalls++;
            return true;
        }

        public bool CaptureLatest()
        {
            CaptureLatestCalls++;
            return true;
        }

        public bool PasteCaptured()
        {
            PasteCapturedCalls++;
            return true;
        }
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
        public string? GetWindowClass(WindowHandle window) => "ClipboardTest";
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
