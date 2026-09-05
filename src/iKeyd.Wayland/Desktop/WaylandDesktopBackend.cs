using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Platform;
using iKeyd.Wayland.Input;

namespace iKeyd.Wayland.Desktop;

public sealed class WaylandDesktopBackend : IDesktopBackend, IBackendCapabilityProvider
{
    private readonly LinuxUInputDevice _uinput;
    private readonly HashSet<DesktopMouseButton> _buttonsDown = [];

    public WaylandDesktopBackend(LinuxUInputDevice uinput)
    {
        _uinput = uinput ?? throw new ArgumentNullException(nameof(uinput));
        Capabilities = new BackendCapabilities([
            BackendCapability.PointerRelative,
            BackendCapability.PointerButtons,
            BackendCapability.PointerScroll,
            BackendCapability.MediaKeys
        ]);
    }

    public BackendCapabilities Capabilities { get; }

    public WindowHandle GetActiveWindow() => Unsupported<WindowHandle>(BackendCapability.WindowQuery);
    public DesktopWindowState GetWindowState(WindowHandle window) => Unsupported<DesktopWindowState>(BackendCapability.WindowState);
    public DesktopRect GetWindowBounds(WindowHandle window) => Unsupported<DesktopRect>(BackendCapability.WindowQuery);
    public DesktopRect GetPrimaryWorkArea() => Unsupported<DesktopRect>(BackendCapability.WindowQuery);
    public string? GetWindowClass(WindowHandle window) => Unsupported<string?>(BackendCapability.WindowQuery);
    public bool IsWindow(WindowHandle window) => Unsupported<bool>(BackendCapability.WindowQuery);
    public void Minimize(WindowHandle window) => Unsupported(BackendCapability.WindowState);
    public void Maximize(WindowHandle window) => Unsupported(BackendCapability.WindowState);
    public void Restore(WindowHandle window) => Unsupported(BackendCapability.WindowState);
    public void MoveResize(WindowHandle window, DesktopRect bounds) => Unsupported(BackendCapability.WindowMoveResize);
    public void Activate(WindowHandle window) => Unsupported(BackendCapability.WindowActivation);
    public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => Unsupported<IReadOnlyList<WindowHandle>>(BackendCapability.WindowQuery);
    public bool IsTopMost(WindowHandle window) => Unsupported<bool>(BackendCapability.WindowTopMost);
    public void SetTopMost(WindowHandle window, bool enabled) => Unsupported(BackendCapability.WindowTopMost);
    public byte? GetOpacity(WindowHandle window) => Unsupported<byte?>(BackendCapability.WindowOpacity);
    public void SetOpacity(WindowHandle window, byte? opacity) => Unsupported(BackendCapability.WindowOpacity);
    public bool HasCaption(WindowHandle window) => Unsupported<bool>(BackendCapability.WindowCaption);
    public void SetCaption(WindowHandle window, bool enabled) => Unsupported(BackendCapability.WindowCaption);

    public DesktopPoint GetPointerPosition() => Unsupported<DesktopPoint>(BackendCapability.PointerAbsolute);
    public void MovePointer(DesktopPoint position) => Unsupported(BackendCapability.PointerAbsolute);

    public void MovePointerBy(int deltaX, int deltaY)
    {
        Capabilities.Require(BackendCapability.PointerRelative);
        _uinput.MovePointerBy(deltaX, deltaY);
    }

    public bool IsMouseButtonDown(DesktopMouseButton button)
    {
        Capabilities.Require(BackendCapability.PointerButtons);
        lock (_buttonsDown)
            return _buttonsDown.Contains(button);
    }

    public void SetMouseButton(DesktopMouseButton button, bool down)
    {
        Capabilities.Require(BackendCapability.PointerButtons);
        _uinput.SetMouseButton(ToLinuxButton(button), down);
        lock (_buttonsDown)
        {
            if (down)
                _buttonsDown.Add(button);
            else
                _buttonsDown.Remove(button);
        }
    }

    public void Click(DesktopMouseButton button)
    {
        Capabilities.Require(BackendCapability.PointerButtons);
        _uinput.ClickMouseButton(ToLinuxButton(button));
    }

    public void ScrollVertical(int wheelDelta, bool controlModifier = false)
    {
        Capabilities.Require(BackendCapability.PointerScroll);
        var clicks = wheelDelta / 120;
        if (clicks == 0 && wheelDelta != 0)
            clicks = Math.Sign(wheelDelta);

        if (controlModifier)
            _uinput.SendKey(new KeyboardKey(0x11, 0), KeyEventKind.Down);
        try
        {
            if (clicks != 0)
                _uinput.ScrollVertical(clicks);
        }
        finally
        {
            if (controlModifier)
                _uinput.SendKey(new KeyboardKey(0x11, 0), KeyEventKind.Up);
        }
    }

    public void SendMediaCommand(DesktopMediaCommand command)
    {
        Capabilities.Require(BackendCapability.MediaKeys);
        _uinput.SendMediaKey(command switch
        {
            DesktopMediaCommand.VolumeUp => LinuxInputCodes.KeyVolumeUp,
            DesktopMediaCommand.VolumeDown => LinuxInputCodes.KeyVolumeDown,
            DesktopMediaCommand.VolumeMute => LinuxInputCodes.KeyMute,
            DesktopMediaCommand.NextTrack => LinuxInputCodes.KeyNextSong,
            DesktopMediaCommand.PreviousTrack => LinuxInputCodes.KeyPreviousSong,
            DesktopMediaCommand.PlayPause => LinuxInputCodes.KeyPlayPause,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        });
    }

    private static ushort ToLinuxButton(DesktopMouseButton button) => button switch
    {
        DesktopMouseButton.Left => LinuxInputCodes.BtnLeft,
        DesktopMouseButton.Right => LinuxInputCodes.BtnRight,
        DesktopMouseButton.Middle => LinuxInputCodes.BtnMiddle,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private static T Unsupported<T>(BackendCapability capability)
        => throw new BackendCapabilityException(capability, "Wayland has no compositor-independent protocol for this desktop/window operation.");

    private static void Unsupported(BackendCapability capability)
        => throw new BackendCapabilityException(capability, "Wayland has no compositor-independent protocol for this desktop/window operation.");
}
