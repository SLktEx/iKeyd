using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Modes;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class FunctionRowLayerRegressionTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    public void Function_keys_follow_generic_H_S_K_A_layers_and_remain_transparent_on_M()
    {
        using (var h = CreateRuntime())
        {
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(h, WindowsKeyMap.Convert, KeyEventKind.Down, 0));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(h, WindowsKeyMap.F1, KeyEventKind.Down, 10));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(h, WindowsKeyMap.F1, KeyEventKind.Up, 11));
            Assert.Equal(
            [
                Event(0xA2, KeyEventKind.Down),
                Event(WindowsKeyMap.F1, KeyEventKind.Down),
                Event(WindowsKeyMap.F1, KeyEventKind.Up),
                Event(0xA2, KeyEventKind.Up)
            ],
            h.Output.Events);
        }

        using (var s = CreateRuntime())
        {
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(s, WindowsKeyMap.Space, KeyEventKind.Down, 0));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(s, (ushort)(WindowsKeyMap.F1 + 1), KeyEventKind.Down, 10));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(s, (ushort)(WindowsKeyMap.F1 + 1), KeyEventKind.Up, 11));
            Assert.Equal(
            [
                Event(0xA0, KeyEventKind.Down),
                Event((ushort)(WindowsKeyMap.F1 + 1), KeyEventKind.Down),
                Event((ushort)(WindowsKeyMap.F1 + 1), KeyEventKind.Up),
                Event(0xA0, KeyEventKind.Up)
            ],
            s.Output.Events);
        }

        using (var k = CreateRuntime())
        {
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(k, WindowsKeyMap.Kana, KeyEventKind.Down, 0));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(k, (ushort)(WindowsKeyMap.F1 + 2), KeyEventKind.Down, 10));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(k, (ushort)(WindowsKeyMap.F1 + 2), KeyEventKind.Up, 11));
            Assert.Equal(
            [
                Event(WindowsKeyMap.LeftWin, KeyEventKind.Down),
                Event((ushort)(WindowsKeyMap.F1 + 2), KeyEventKind.Down),
                Event((ushort)(WindowsKeyMap.F1 + 2), KeyEventKind.Up),
                Event(WindowsKeyMap.LeftWin, KeyEventKind.Up)
            ],
            k.Output.Events);
        }

        using (var a = CreateRuntime())
        {
            const ushort leftAlt = 0xA4;
            Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(a, leftAlt, KeyEventKind.Down, 0));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(a, WindowsKeyMap.Kana, KeyEventKind.Down, 5));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(a, (ushort)(WindowsKeyMap.F1 + 3), KeyEventKind.Down, 10));
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(a, (ushort)(WindowsKeyMap.F1 + 3), KeyEventKind.Up, 11));
            Assert.Equal(
            [
                Event(leftAlt, KeyEventKind.Down),
                Event((ushort)(WindowsKeyMap.F1 + 3), KeyEventKind.Down),
                Event((ushort)(WindowsKeyMap.F1 + 3), KeyEventKind.Up),
                Event(leftAlt, KeyEventKind.Up)
            ],
            a.Output.Events);
        }

        using (var m = CreateRuntime())
        {
            Assert.Equal(KeyboardDisposition.Suppress, Dispatch(m, WindowsKeyMap.NonConvert, KeyEventKind.Down, 0));
            Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(m, (ushort)(WindowsKeyMap.F1 + 4), KeyEventKind.Down, 10));
            Assert.Equal(KeyboardDisposition.PassThrough, Dispatch(m, (ushort)(WindowsKeyMap.F1 + 4), KeyEventKind.Up, 11));
            Assert.Empty(m.Output.Events);
            Assert.Empty(m.Output.Text);
        }
    }

    private static RuntimeFixture CreateRuntime()
    {
        var configuration = IKeydConfiguration.Load(ProfilePath) with { StartupMode = InputMode.R };
        var keyboardState = new KeyboardState();
        var output = new RecordingKeyboardOutput();
        var runtime = new IKeydRuntimeHandler(
            configuration,
            new InactiveInputMethod(),
            keyboardState,
            new LegacySendOutput(output),
            new NullDesktopBackend());
        return new RuntimeFixture(runtime, keyboardState, output);
    }

    private static KeyboardDisposition Dispatch(
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
        return fixture.Runtime.OnKeyboardEvent(keyboardEvent);
    }

    private static RecordedKeyboardEvent Event(ushort virtualKey, KeyEventKind kind)
        => new(WindowsKeyMap.Keyboard(virtualKey), kind);

    private sealed record RuntimeFixture(
        IKeydRuntimeHandler Runtime,
        KeyboardState KeyboardState,
        RecordingKeyboardOutput Output) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class InactiveInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => false;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<RecordedKeyboardEvent> Events { get; } = [];
        public List<string> Text { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add(new RecordedKeyboardEvent(key, kind));
        public void SendKeyPress(KeyboardKey key)
        {
            SendKey(key, KeyEventKind.Down);
            SendKey(key, KeyEventKind.Up);
        }
        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class NullDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "FunctionRowLayerTest";
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

    private readonly record struct RecordedKeyboardEvent(KeyboardKey Key, KeyEventKind Kind);
}
