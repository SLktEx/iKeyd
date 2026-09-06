using iKeyd.Core.Desktop;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Desktop;

public sealed class WindowsDesktopBackend : IDesktopBackend
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000L;
    private const long WsExTopMost = 0x00000008L;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int SwMinimize = 6;
    private const int SwMaximize = 3;
    private const int SwRestore = 9;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;
    private const ushort VkControl = 0x11;
    private const ushort VkVolumeMute = 0xAD;
    private const ushort VkVolumeDown = 0xAE;
    private const ushort VkVolumeUp = 0xAF;
    private const ushort VkMediaNext = 0xB0;
    private const ushort VkMediaPrevious = 0xB1;
    private const ushort VkMediaPlayPause = 0xB3;

    private readonly IWindowsDesktopNative _native;
    private readonly IKeyboardOutput _keyboard;

    public WindowsDesktopBackend()
        : this(new Win32DesktopNative(), new WindowsKeyboardOutput())
    {
    }

    internal WindowsDesktopBackend(IWindowsDesktopNative native, IKeyboardOutput keyboard)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
    }

    public WindowHandle GetActiveWindow() => new(_native.GetForegroundWindow());

    public DesktopWindowState GetWindowState(WindowHandle window)
    {
        EnsureWindow(window);
        if (_native.IsIconic(window.Value))
            return DesktopWindowState.Minimized;
        if (_native.IsZoomed(window.Value))
            return DesktopWindowState.Maximized;
        return DesktopWindowState.Normal;
    }

    public DesktopRect GetWindowBounds(WindowHandle window)
    {
        EnsureWindow(window);
        if (!_native.GetWindowRect(window.Value, out var rect))
            throw new InvalidOperationException("GetWindowRect failed.");
        return rect.ToDesktopRect();
    }

    public DesktopRect GetPrimaryWorkArea()
    {
        if (!_native.TryGetPrimaryWorkArea(out var rect))
            throw new InvalidOperationException("Failed to get the primary work area.");
        return rect.ToDesktopRect();
    }

    public string? GetWindowClass(WindowHandle window)
    {
        EnsureWindow(window);
        return _native.GetWindowClass(window.Value);
    }

    public bool IsWindow(WindowHandle window)
        => !window.IsEmpty && _native.IsWindow(window.Value);

    public void Minimize(WindowHandle window)
    {
        EnsureWindow(window);
        _native.ShowWindow(window.Value, SwMinimize);
    }

    public void Maximize(WindowHandle window)
    {
        EnsureWindow(window);
        _native.ShowWindow(window.Value, SwMaximize);
    }

    public void Restore(WindowHandle window)
    {
        EnsureWindow(window);
        _native.ShowWindow(window.Value, SwRestore);
    }

    public void MoveResize(WindowHandle window, DesktopRect bounds)
    {
        EnsureWindow(window);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Window width and height must be positive.");
        if (!_native.MoveWindow(window.Value, bounds.X, bounds.Y, bounds.Width, bounds.Height))
            throw new InvalidOperationException("MoveWindow failed.");
    }

    public void Activate(WindowHandle window)
    {
        EnsureWindow(window);
        if (!_native.SetForegroundWindow(window.Value))
            throw new InvalidOperationException("SetForegroundWindow failed.");
    }

    public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows()
        => _native.EnumerateTopLevelWindows().Select(value => new WindowHandle(value)).ToArray();

    public bool IsTopMost(WindowHandle window)
    {
        EnsureWindow(window);
        return (GetWindowBits(window, GwlExStyle) & WsExTopMost) != 0;
    }

    public void SetTopMost(WindowHandle window, bool enabled)
    {
        EnsureWindow(window);
        var insertAfter = enabled ? new nint(-1) : new nint(-2);
        if (!_native.SetWindowPos(window.Value, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
            throw new InvalidOperationException("SetWindowPos failed while changing top-most state.");
    }

    public byte? GetOpacity(WindowHandle window)
    {
        EnsureWindow(window);
        if ((GetWindowBits(window, GwlExStyle) & WsExLayered) == 0)
            return null;
        if (!_native.GetLayeredWindowAttributes(window.Value, out var alpha, out var flags))
            return null;
        return (flags & LwaAlpha) != 0 ? alpha : null;
    }

    public void SetOpacity(WindowHandle window, byte? opacity)
    {
        EnsureWindow(window);
        var style = GetWindowBits(window, GwlExStyle);

        if (opacity is null || opacity.Value == byte.MaxValue)
        {
            if ((style & WsExLayered) != 0)
                SetWindowBits(window, GwlExStyle, style & ~WsExLayered);
            return;
        }

        if ((style & WsExLayered) == 0)
            SetWindowBits(window, GwlExStyle, style | WsExLayered);
        if (!_native.SetLayeredWindowAttributes(window.Value, opacity.Value, LwaAlpha))
            throw new InvalidOperationException("SetLayeredWindowAttributes failed.");
    }

    public bool HasCaption(WindowHandle window)
    {
        EnsureWindow(window);
        return (GetWindowBits(window, GwlStyle) & WsCaption) == WsCaption;
    }

    public void SetCaption(WindowHandle window, bool enabled)
    {
        EnsureWindow(window);
        var style = GetWindowBits(window, GwlStyle);
        var next = enabled ? style | WsCaption : style & ~WsCaption;
        if (next == style)
            return;
        SetWindowBits(window, GwlStyle, next);
        if (!_native.SetWindowPos(window.Value, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged))
            throw new InvalidOperationException("SetWindowPos failed while refreshing window style.");
    }

    public DesktopPoint GetPointerPosition()
    {
        if (!_native.GetCursorPos(out var point))
            throw new InvalidOperationException("GetCursorPos failed.");
        return new DesktopPoint(point.X, point.Y);
    }

    public void MovePointer(DesktopPoint position)
    {
        if (!_native.SetCursorPos(position.X, position.Y))
            throw new InvalidOperationException("SetCursorPos failed.");
    }

    public void MovePointerBy(int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0)
            return;
        _native.SendMouseMove(deltaX, deltaY);
    }

    public bool IsMouseButtonDown(DesktopMouseButton button)
        => (_native.GetAsyncKeyState(ToVirtualKey(button)) & 0x8000) != 0;

    public void SetMouseButton(DesktopMouseButton button, bool down)
        => _native.SendMouseButton(button, down);

    public void Click(DesktopMouseButton button)
    {
        SetMouseButton(button, true);
        SetMouseButton(button, false);
    }

    public void ScrollVertical(int wheelDelta, bool controlModifier = false)
    {
        if (wheelDelta == 0)
            return;

        if (controlModifier)
            _keyboard.SendKey(new KeyboardKey(VkControl, 0), KeyEventKind.Down);
        try
        {
            _native.SendMouseWheel(wheelDelta);
        }
        finally
        {
            if (controlModifier)
                _keyboard.SendKey(new KeyboardKey(VkControl, 0), KeyEventKind.Up);
        }
    }

    public void SendMediaCommand(DesktopMediaCommand command)
    {
        var virtualKey = command switch
        {
            DesktopMediaCommand.VolumeUp => VkVolumeUp,
            DesktopMediaCommand.VolumeDown => VkVolumeDown,
            DesktopMediaCommand.VolumeMute => VkVolumeMute,
            DesktopMediaCommand.NextTrack => VkMediaNext,
            DesktopMediaCommand.PreviousTrack => VkMediaPrevious,
            DesktopMediaCommand.PlayPause => VkMediaPlayPause,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        _keyboard.SendKeyPress(new KeyboardKey(virtualKey, 0, true));
    }

    private static int ToVirtualKey(DesktopMouseButton button) => button switch
    {
        DesktopMouseButton.Left => VkLButton,
        DesktopMouseButton.Right => VkRButton,
        DesktopMouseButton.Middle => VkMButton,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private long GetWindowBits(WindowHandle window, int index)
        => _native.GetWindowLongPtr(window.Value, index).ToInt64();

    private void SetWindowBits(WindowHandle window, int index, long value)
    {
        _native.ClearLastError();
        var previous = _native.SetWindowLongPtr(window.Value, index, new nint(value));
        if (previous == 0 && _native.GetLastError() != 0)
            throw new InvalidOperationException("SetWindowLongPtr failed.");
    }

    private void EnsureWindow(WindowHandle window)
    {
        if (!IsWindow(window))
            throw new ArgumentException("The window handle is empty or invalid.", nameof(window));
    }
}
