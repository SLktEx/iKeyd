namespace iKeyd.Core.Desktop;

public readonly record struct WindowHandle(nint Value)
{
    public bool IsEmpty => Value == 0;
}

public readonly record struct DesktopPoint(int X, int Y);

public readonly record struct DesktopRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public enum DesktopWindowState
{
    Normal,
    Minimized,
    Maximized
}

public enum DesktopMouseButton
{
    Left,
    Right,
    Middle
}

public enum DesktopMediaCommand
{
    VolumeUp,
    VolumeDown,
    VolumeMute,
    NextTrack,
    PreviousTrack,
    PlayPause
}

public interface IDesktopBackend
{
    WindowHandle GetActiveWindow();
    DesktopWindowState GetWindowState(WindowHandle window);
    DesktopRect GetWindowBounds(WindowHandle window);
    DesktopRect GetPrimaryWorkArea();
    string? GetWindowClass(WindowHandle window);
    bool IsWindow(WindowHandle window);

    void Minimize(WindowHandle window);
    void Maximize(WindowHandle window);
    void Restore(WindowHandle window);
    void MoveResize(WindowHandle window, DesktopRect bounds);
    void Activate(WindowHandle window);
    IReadOnlyList<WindowHandle> EnumerateTopLevelWindows();

    bool IsTopMost(WindowHandle window);
    void SetTopMost(WindowHandle window, bool enabled);
    byte? GetOpacity(WindowHandle window);
    void SetOpacity(WindowHandle window, byte? opacity);
    bool HasCaption(WindowHandle window);
    void SetCaption(WindowHandle window, bool enabled);

    DesktopPoint GetPointerPosition();
    void MovePointer(DesktopPoint position);
    void MovePointerBy(int deltaX, int deltaY);
    bool IsMouseButtonDown(DesktopMouseButton button);
    void SetMouseButton(DesktopMouseButton button, bool down);
    void Click(DesktopMouseButton button);
    void ScrollVertical(int wheelDelta, bool controlModifier = false);

    void SendMediaCommand(DesktopMediaCommand command);
}
