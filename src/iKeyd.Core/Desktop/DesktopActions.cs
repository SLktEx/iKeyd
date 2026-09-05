namespace iKeyd.Core.Desktop;

public enum DesktopPlacement
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf
}

public sealed class DesktopActionService
{
    private readonly IDesktopBackend _desktop;

    public DesktopActionService(IDesktopBackend desktop)
        => _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));

    public void MinimizeActive()
        => WithActiveWindow(_desktop.Minimize);

    public void ToggleMaximizeActive()
    {
        var window = RequireActiveWindow();
        if (_desktop.GetWindowState(window) == DesktopWindowState.Normal)
            _desktop.Maximize(window);
        else
            _desktop.Restore(window);
    }

    public void PlaceActive(DesktopPlacement placement)
    {
        var window = RequireActiveWindow();
        var area = _desktop.GetPrimaryWorkArea();
        var leftWidth = area.Width / 2;
        var topHeight = area.Height / 2;
        var bounds = placement switch
        {
            DesktopPlacement.LeftHalf => new DesktopRect(area.X, area.Y, leftWidth, area.Height),
            DesktopPlacement.RightHalf => new DesktopRect(area.X + leftWidth, area.Y, area.Width - leftWidth, area.Height),
            DesktopPlacement.TopHalf => new DesktopRect(area.X, area.Y, area.Width, topHeight),
            DesktopPlacement.BottomHalf => new DesktopRect(area.X, area.Y + topHeight, area.Width, area.Height - topHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(placement))
        };
        _desktop.MoveResize(window, bounds);
    }

    public void ToggleTopMostActive()
    {
        var window = RequireActiveWindow();
        _desktop.SetTopMost(window, !_desktop.IsTopMost(window));
    }

    public void AdjustOpacityActive(int delta)
    {
        var window = RequireActiveWindow();
        var current = _desktop.GetOpacity(window) ?? byte.MaxValue;
        var next = current + delta;
        if (next >= byte.MaxValue)
            _desktop.SetOpacity(window, null);
        else
            _desktop.SetOpacity(window, (byte)Math.Max(15, next));
    }

    public void ToggleCaptionActive()
    {
        var window = RequireActiveWindow();
        _desktop.SetCaption(window, !_desktop.HasCaption(window));
    }

    public void ActivateBottomWindowOfActiveClass()
    {
        var active = RequireActiveWindow();
        var className = _desktop.GetWindowClass(active);
        if (string.IsNullOrEmpty(className))
            return;

        WindowHandle candidate = default;
        foreach (var window in _desktop.EnumerateTopLevelWindows())
        {
            if (!_desktop.IsWindow(window))
                continue;
            if (string.Equals(_desktop.GetWindowClass(window), className, StringComparison.Ordinal))
                candidate = window;
        }

        if (!candidate.IsEmpty && candidate != active)
            _desktop.Activate(candidate);
    }

    public void MovePointerToActiveWindowCorner(bool bottomRight)
    {
        var bounds = _desktop.GetWindowBounds(RequireActiveWindow());
        var x = bottomRight ? bounds.Right - 1 : bounds.X + 1;
        var y = bottomRight ? bounds.Bottom - 1 : bounds.Y + 1;
        _desktop.MovePointer(new DesktopPoint(x, y));
    }

    public void ToggleMouseButton(DesktopMouseButton button)
        => _desktop.SetMouseButton(button, !_desktop.IsMouseButtonDown(button));

    private WindowHandle RequireActiveWindow()
    {
        var window = _desktop.GetActiveWindow();
        if (!_desktop.IsWindow(window))
            throw new InvalidOperationException("There is no active top-level window.");
        return window;
    }

    private void WithActiveWindow(Action<WindowHandle> action)
        => action(RequireActiveWindow());
}

public sealed class WindowGroupController
{
    private readonly IDesktopBackend _desktop;
    private readonly List<WindowHandle> _windows = [];
    private int _nextIndex;

    public WindowGroupController(IDesktopBackend desktop)
        => _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));

    public int GroupNumber { get; private set; } = 1;
    public IReadOnlyList<WindowHandle> Windows => _windows;

    public void ToggleActiveWindow()
    {
        PruneInvalidWindows();
        var active = _desktop.GetActiveWindow();
        if (!_desktop.IsWindow(active))
            return;

        var index = _windows.IndexOf(active);
        if (index >= 0)
        {
            _windows.RemoveAt(index);
            GroupNumber++;
            _nextIndex = 0;
        }
        else
        {
            _windows.Add(active);
        }
    }

    public bool ActivateNext()
    {
        PruneInvalidWindows();
        if (_windows.Count == 0)
            return false;

        if (_nextIndex >= _windows.Count)
            _nextIndex = 0;
        var window = _windows[_nextIndex];
        _nextIndex = (_nextIndex + 1) % _windows.Count;
        _desktop.Activate(window);
        return true;
    }

    public void ResetAndAdvance()
    {
        GroupNumber++;
        _windows.Clear();
        _nextIndex = 0;
    }

    private void PruneInvalidWindows()
    {
        _windows.RemoveAll(window => !_desktop.IsWindow(window));
        if (_windows.Count == 0 || _nextIndex >= _windows.Count)
            _nextIndex = 0;
    }
}
