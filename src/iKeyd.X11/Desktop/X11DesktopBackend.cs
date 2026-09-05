using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Platform;
using iKeyd.Linux.Input;
using iKeyd.X11.Interop;

namespace iKeyd.X11.Desktop;

public sealed class X11DesktopBackend : IDesktopBackend, IBackendCapabilityProvider, IDisposable
{
    private const int WmStateRemove = 0;
    private const int WmStateAdd = 1;
    private readonly X11Connection _x;
    private readonly bool _ownsConnection;
    private readonly ILinuxVirtualInput? _virtualInput;

    public X11DesktopBackend(X11Connection? connection = null, ILinuxVirtualInput? virtualInput = null)
    {
        _x = connection ?? new X11Connection();
        _ownsConnection = connection is null;
        _virtualInput = virtualInput;

        var supported = _x.Capabilities.Supported.ToList();
        supported.Add(BackendCapability.PointerRelative);
        if (_virtualInput is not null)
            supported.Add(BackendCapability.MediaKeys);
        Capabilities = new BackendCapabilities(supported);
    }

    public BackendCapabilities Capabilities { get; }

    public WindowHandle GetActiveWindow()
    {
        var values = _x.GetProperty(_x.Root, "_NET_ACTIVE_WINDOW", 1);
        return values.Length == 0 ? default : new WindowHandle(unchecked((nint)values[0]));
    }

    public DesktopWindowState GetWindowState(WindowHandle window)
    {
        RequireWindow(window);
        var states = _x.GetProperty(ToXid(window), "_NET_WM_STATE");
        if (states.Contains(_x.Atom("_NET_WM_STATE_HIDDEN")))
            return DesktopWindowState.Minimized;
        if (states.Contains(_x.Atom("_NET_WM_STATE_MAXIMIZED_HORZ")) &&
            states.Contains(_x.Atom("_NET_WM_STATE_MAXIMIZED_VERT")))
            return DesktopWindowState.Maximized;

        if (X11Native.XGetWindowAttributes(_x.Display, ToXid(window), out var attributes) != 0 && attributes.MapState != X11Native.IsViewable)
            return DesktopWindowState.Minimized;
        return DesktopWindowState.Normal;
    }

    public DesktopRect GetWindowBounds(WindowHandle window)
    {
        RequireWindow(window);
        var xid = ToXid(window);
        if (X11Native.XGetWindowAttributes(_x.Display, xid, out var attributes) == 0)
            throw new InvalidOperationException("XGetWindowAttributes failed.");
        if (X11Native.XTranslateCoordinates(_x.Display, xid, _x.Root, 0, 0, out var x, out var y, out _) == 0)
        {
            x = attributes.X;
            y = attributes.Y;
        }
        return new DesktopRect(x, y, attributes.Width, attributes.Height);
    }

    public DesktopRect GetPrimaryWorkArea()
    {
        var work = _x.GetProperty(_x.Root, "_NET_WORKAREA");
        var desktop = _x.GetProperty(_x.Root, "_NET_CURRENT_DESKTOP", 1);
        var index = desktop.Length == 0 ? 0 : (int)Math.Min(desktop[0], (nuint)int.MaxValue);
        var offset = index * 4;
        if (work.Length >= offset + 4)
            return new DesktopRect((int)work[offset], (int)work[offset + 1], (int)work[offset + 2], (int)work[offset + 3]);

        if (X11Native.XGetWindowAttributes(_x.Display, _x.Root, out var root) == 0)
            throw new InvalidOperationException("Could not query X11 root window geometry.");
        return new DesktopRect(0, 0, root.Width, root.Height);
    }

    public string? GetWindowClass(WindowHandle window)
    {
        RequireWindow(window);
        return _x.GetWindowClass(ToXid(window));
    }

    public bool IsWindow(WindowHandle window)
        => !window.IsEmpty && X11Native.XGetWindowAttributes(_x.Display, ToXid(window), out _) != 0;

    public void Minimize(WindowHandle window)
    {
        RequireWindow(window);
        X11Native.XIconifyWindow(_x.Display, ToXid(window), _x.Screen);
        X11Native.XFlush(_x.Display);
    }

    public void Maximize(WindowHandle window)
    {
        RequireWindow(window);
        SendWmState(window, WmStateAdd, "_NET_WM_STATE_MAXIMIZED_HORZ", "_NET_WM_STATE_MAXIMIZED_VERT");
    }

    public void Restore(WindowHandle window)
    {
        RequireWindow(window);
        SendWmState(window, WmStateRemove, "_NET_WM_STATE_MAXIMIZED_HORZ", "_NET_WM_STATE_MAXIMIZED_VERT");
        X11Native.XMapRaised(_x.Display, ToXid(window));
        X11Native.XFlush(_x.Display);
    }

    public void MoveResize(WindowHandle window, DesktopRect bounds)
    {
        RequireWindow(window);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        X11Native.XMoveResizeWindow(_x.Display, ToXid(window), bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height);
        X11Native.XFlush(_x.Display);
    }

    public void Activate(WindowHandle window)
    {
        RequireWindow(window);
        _x.SendClientMessage(ToXid(window), "_NET_ACTIVE_WINDOW", 2, 0, 0, 0, 0);
        X11Native.XRaiseWindow(_x.Display, ToXid(window));
        X11Native.XSetInputFocus(_x.Display, ToXid(window), X11Native.RevertToParent, 0);
        X11Native.XFlush(_x.Display);
    }

    public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows()
        => _x.GetProperty(_x.Root, "_NET_CLIENT_LIST")
            .Where(value => value != 0)
            .Select(value => new WindowHandle(unchecked((nint)value)))
            .ToArray();

    public bool IsTopMost(WindowHandle window)
    {
        RequireWindow(window);
        return _x.GetProperty(ToXid(window), "_NET_WM_STATE").Contains(_x.Atom("_NET_WM_STATE_ABOVE"));
    }

    public void SetTopMost(WindowHandle window, bool enabled)
    {
        RequireWindow(window);
        SendWmState(window, enabled ? WmStateAdd : WmStateRemove, "_NET_WM_STATE_ABOVE");
    }

    public byte? GetOpacity(WindowHandle window)
    {
        RequireWindow(window);
        var values = _x.GetProperty(ToXid(window), "_NET_WM_WINDOW_OPACITY", 1);
        if (values.Length == 0) return null;
        var value = (uint)values[0];
        return (byte)Math.Clamp((long)Math.Round(value * 255d / uint.MaxValue), 0, 255);
    }

    public void SetOpacity(WindowHandle window, byte? opacity)
    {
        RequireWindow(window);
        if (opacity is null || opacity == 255)
            _x.DeleteProperty(ToXid(window), "_NET_WM_WINDOW_OPACITY");
        else
            _x.SetCardinal32(ToXid(window), "_NET_WM_WINDOW_OPACITY", (uint)((ulong)opacity.Value * uint.MaxValue / 255UL));
    }

    public bool HasCaption(WindowHandle window) => Unsupported<bool>(BackendCapability.WindowCaption);
    public void SetCaption(WindowHandle window, bool enabled) => Unsupported(BackendCapability.WindowCaption);

    public DesktopPoint GetPointerPosition()
    {
        if (X11Native.XQueryPointer(_x.Display, _x.Root, out _, out _, out var x, out var y, out _, out _, out _) == 0)
            throw new InvalidOperationException("XQueryPointer failed.");
        return new DesktopPoint(x, y);
    }

    public void MovePointer(DesktopPoint position)
    {
        if (X11Native.XTestFakeMotionEvent(_x.Display, _x.Screen, position.X, position.Y, 0) == 0)
            throw new InvalidOperationException("XTEST motion injection failed.");
        X11Native.XFlush(_x.Display);
    }

    public void MovePointerBy(int deltaX, int deltaY)
    {
        var current = GetPointerPosition();
        MovePointer(new DesktopPoint(current.X + deltaX, current.Y + deltaY));
    }

    public bool IsMouseButtonDown(DesktopMouseButton button)
    {
        if (X11Native.XQueryPointer(_x.Display, _x.Root, out _, out _, out _, out _, out _, out _, out var mask) == 0)
            throw new InvalidOperationException("XQueryPointer failed.");
        var bit = button switch
        {
            DesktopMouseButton.Left => X11Native.Button1Mask,
            DesktopMouseButton.Middle => X11Native.Button2Mask,
            DesktopMouseButton.Right => X11Native.Button3Mask,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };
        return ((int)mask & bit) != 0;
    }

    public void SetMouseButton(DesktopMouseButton button, bool down)
    {
        Capabilities.Require(BackendCapability.PointerButtons, "XTEST is required.");
        if (X11Native.XTestFakeButtonEvent(_x.Display, ToXButton(button), down ? 1 : 0, 0) == 0)
            throw new InvalidOperationException("XTEST button injection failed.");
        X11Native.XFlush(_x.Display);
    }

    public void Click(DesktopMouseButton button)
    {
        SetMouseButton(button, true);
        SetMouseButton(button, false);
    }

    public void ScrollVertical(int wheelDelta, bool controlModifier = false)
    {
        Capabilities.Require(BackendCapability.PointerScroll, "XTEST is required.");
        var clicks = wheelDelta / 120;
        if (clicks == 0 && wheelDelta != 0) clicks = Math.Sign(wheelDelta);
        if (controlModifier && _virtualInput is null)
            throw new BackendCapabilityException(BackendCapability.KeyboardOutput, "Ctrl+scroll needs the shared Linux virtual keyboard output.");

        if (controlModifier) _virtualInput!.SendKey(new KeyboardKey(0x11, 0), KeyEventKind.Down);
        try
        {
            var button = clicks >= 0 ? 4u : 5u;
            for (var i = 0; i < Math.Abs(clicks); i++)
            {
                X11Native.XTestFakeButtonEvent(_x.Display, button, 1, 0);
                X11Native.XTestFakeButtonEvent(_x.Display, button, 0, 0);
            }
            X11Native.XFlush(_x.Display);
        }
        finally
        {
            if (controlModifier) _virtualInput!.SendKey(new KeyboardKey(0x11, 0), KeyEventKind.Up);
        }
    }

    public void SendMediaCommand(DesktopMediaCommand command)
    {
        if (_virtualInput is null)
            throw new BackendCapabilityException(BackendCapability.MediaKeys, "Media keys require the shared Linux uinput output.");
        _virtualInput.SendMediaKey(command switch
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

    public void Dispose()
    {
        if (_ownsConnection) _x.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SendWmState(WindowHandle window, int action, string first, string? second = null)
        => _x.SendClientMessage(ToXid(window), "_NET_WM_STATE", action, unchecked((nint)_x.Atom(first)), second is null ? 0 : unchecked((nint)_x.Atom(second)), 2, 0);

    private static uint ToXButton(DesktopMouseButton button) => button switch
    {
        DesktopMouseButton.Left => 1,
        DesktopMouseButton.Middle => 2,
        DesktopMouseButton.Right => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private void RequireWindow(WindowHandle window)
    {
        if (!IsWindow(window)) throw new ArgumentException("X11 window handle is invalid.", nameof(window));
    }

    private static nuint ToXid(WindowHandle window) => unchecked((nuint)window.Value);
    private static T Unsupported<T>(BackendCapability capability) => throw new BackendCapabilityException(capability, "No portable EWMH/ICCCM operation is implemented for this feature.");
    private static void Unsupported(BackendCapability capability) => throw new BackendCapabilityException(capability, "No portable EWMH/ICCCM operation is implemented for this feature.");
}
