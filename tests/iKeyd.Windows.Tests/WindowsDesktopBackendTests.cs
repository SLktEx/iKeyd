using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Windows.Desktop;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsDesktopBackendTests
{
    private static readonly WindowHandle Window = new(42);

    [Fact]
    public void Window_state_prefers_minimized_then_maximized()
    {
        var native = new FakeNative { Iconic = true, Zoomed = true };
        var backend = Create(native);
        Assert.Equal(DesktopWindowState.Minimized, backend.GetWindowState(Window));

        native.Iconic = false;
        Assert.Equal(DesktopWindowState.Maximized, backend.GetWindowState(Window));

        native.Zoomed = false;
        Assert.Equal(DesktopWindowState.Normal, backend.GetWindowState(Window));
    }

    [Fact]
    public void Topmost_uses_hwnd_topmost_and_hwnd_notopmost()
    {
        var native = new FakeNative();
        var backend = Create(native);

        backend.SetTopMost(Window, true);
        Assert.Equal(new nint(-1), native.LastWindowPosInsertAfter);

        backend.SetTopMost(Window, false);
        Assert.Equal(new nint(-2), native.LastWindowPosInsertAfter);
    }

    [Fact]
    public void Opacity_adds_layered_style_and_can_turn_transparency_off()
    {
        var native = new FakeNative { ExStyle = 0 };
        var backend = Create(native);

        backend.SetOpacity(Window, 225);
        Assert.NotEqual(0, native.ExStyle & 0x00080000L);
        Assert.Equal((byte)225, native.LayeredAlpha);

        backend.SetOpacity(Window, null);
        Assert.Equal(0, native.ExStyle & 0x00080000L);
    }

    [Fact]
    public void Caption_toggle_updates_style_and_requests_frame_change()
    {
        var native = new FakeNative { Style = 0x00C00000L };
        var backend = Create(native);

        backend.SetCaption(Window, false);

        Assert.Equal(0, native.Style & 0x00C00000L);
        Assert.NotEqual(0u, native.LastWindowPosFlags & 0x0020u);
    }

    [Fact]
    public void Click_emits_mouse_down_then_up()
    {
        var native = new FakeNative();
        var backend = Create(native);

        backend.Click(DesktopMouseButton.Right);

        Assert.Equal(
            [(DesktopMouseButton.Right, true), (DesktopMouseButton.Right, false)],
            native.MouseButtons);
    }

    [Fact]
    public void Control_scroll_wraps_wheel_with_control_down_and_up()
    {
        var native = new FakeNative();
        var keyboard = new RecordingKeyboardOutput();
        var backend = new WindowsDesktopBackend(native, keyboard);

        backend.ScrollVertical(120, controlModifier: true);

        Assert.Equal([120], native.Wheels);
        Assert.Equal(2, keyboard.Events.Count);
        Assert.Equal(new KeyboardKey(0x11, 0), keyboard.Events[0].Key);
        Assert.Equal(KeyEventKind.Down, keyboard.Events[0].Kind);
        Assert.Equal(KeyEventKind.Up, keyboard.Events[1].Kind);
    }

    [Fact]
    public void Media_commands_use_existing_keyboard_output_path()
    {
        var keyboard = new RecordingKeyboardOutput();
        var backend = new WindowsDesktopBackend(new FakeNative(), keyboard);

        backend.SendMediaCommand(DesktopMediaCommand.VolumeUp);
        backend.SendMediaCommand(DesktopMediaCommand.PlayPause);

        Assert.Equal(new KeyboardKey(0xAF, 0, true), keyboard.Presses[0]);
        Assert.Equal(new KeyboardKey(0xB3, 0, true), keyboard.Presses[1]);
    }

    [Fact]
    public void Relative_pointer_move_is_resolved_against_current_cursor_position()
    {
        var native = new FakeNative { Cursor = new NativePoint(100, 200) };
        var backend = Create(native);

        backend.MovePointerBy(-30, 10);

        Assert.Equal(70, native.Cursor.X);
        Assert.Equal(210, native.Cursor.Y);
    }

    private static WindowsDesktopBackend Create(FakeNative native)
        => new(native, new RecordingKeyboardOutput());

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, KeyEventKind Kind)> Events { get; } = [];
        public List<KeyboardKey> Presses { get; } = [];

        public void SendKey(KeyboardKey key, KeyEventKind kind) => Events.Add((key, kind));
        public void SendKeyPress(KeyboardKey key) => Presses.Add(key);
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class FakeNative : IWindowsDesktopNative
    {
        public bool Iconic { get; set; }
        public bool Zoomed { get; set; }
        public long Style { get; set; }
        public long ExStyle { get; set; }
        public byte LayeredAlpha { get; set; } = byte.MaxValue;
        public uint LayeredFlags { get; set; } = 0x2;
        public NativePoint Cursor { get; set; } = new(0, 0);
        public nint LastWindowPosInsertAfter { get; private set; }
        public uint LastWindowPosFlags { get; private set; }
        public List<(DesktopMouseButton Button, bool Down)> MouseButtons { get; } = [];
        public List<int> Wheels { get; } = [];
        public int LastError { get; set; }

        public nint GetForegroundWindow() => Window.Value;
        public bool IsIconic(nint window) => Iconic;
        public bool IsZoomed(nint window) => Zoomed;
        public bool GetWindowRect(nint window, out NativeRect rect)
        {
            rect = new NativeRect(10, 20, 210, 320);
            return true;
        }
        public bool TryGetPrimaryWorkArea(out NativeRect rect)
        {
            rect = new NativeRect(0, 0, 1920, 1040);
            return true;
        }
        public string? GetWindowClass(nint window) => "FakeWindow";
        public bool IsWindow(nint window) => window != 0;
        public bool ShowWindow(nint window, int command) => true;
        public bool MoveWindow(nint window, int x, int y, int width, int height) => true;
        public bool SetForegroundWindow(nint window) => true;
        public IReadOnlyList<nint> EnumerateTopLevelWindows() => [Window.Value];
        public nint GetWindowLongPtr(nint window, int index)
            => index == -16 ? unchecked((nint)Style) : unchecked((nint)ExStyle);
        public nint SetWindowLongPtr(nint window, int index, nint value)
        {
            var previous = GetWindowLongPtr(window, index);
            if (index == -16)
                Style = value.ToInt64();
            else
                ExStyle = value.ToInt64();
            return previous;
        }
        public void ClearLastError() => LastError = 0;
        public int GetLastError() => LastError;
        public bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags)
        {
            LastWindowPosInsertAfter = insertAfter;
            LastWindowPosFlags = flags;
            return true;
        }
        public bool GetLayeredWindowAttributes(nint window, out byte alpha, out uint flags)
        {
            alpha = LayeredAlpha;
            flags = LayeredFlags;
            return true;
        }
        public bool SetLayeredWindowAttributes(nint window, byte alpha, uint flags)
        {
            LayeredAlpha = alpha;
            LayeredFlags = flags;
            return true;
        }
        public bool GetCursorPos(out NativePoint point)
        {
            point = Cursor;
            return true;
        }
        public bool SetCursorPos(int x, int y)
        {
            Cursor = new NativePoint(x, y);
            return true;
        }
        public short GetAsyncKeyState(int virtualKey) => 0;
        public void SendMouseButton(DesktopMouseButton button, bool down) => MouseButtons.Add((button, down));
        public void SendMouseWheel(int wheelDelta) => Wheels.Add(wheelDelta);
    }
}
