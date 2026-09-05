using iKeyd.Core.Desktop;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class DesktopActionServiceTests
{
    private static readonly WindowHandle Active = new(10);

    [Fact]
    public void Toggle_maximize_maximizes_normal_window_and_restores_non_normal_window()
    {
        var backend = new FakeDesktopBackend { ActiveWindow = Active, WindowState = DesktopWindowState.Normal };
        var actions = new DesktopActionService(backend);

        actions.ToggleMaximizeActive();
        Assert.Equal(Active, backend.Maximized);

        backend.WindowState = DesktopWindowState.Maximized;
        actions.ToggleMaximizeActive();
        Assert.Equal(Active, backend.Restored);
    }

    [Theory]
    [InlineData(DesktopPlacement.LeftHalf, 0, 0, 500, 800)]
    [InlineData(DesktopPlacement.RightHalf, 500, 0, 501, 800)]
    [InlineData(DesktopPlacement.TopHalf, 0, 0, 1001, 400)]
    [InlineData(DesktopPlacement.BottomHalf, 0, 400, 1001, 400)]
    public void Placement_uses_entire_primary_work_area_without_losing_odd_pixels(
        DesktopPlacement placement, int x, int y, int width, int height)
    {
        var backend = new FakeDesktopBackend
        {
            ActiveWindow = Active,
            WorkArea = new DesktopRect(0, 0, 1001, 800)
        };
        var actions = new DesktopActionService(backend);

        actions.PlaceActive(placement);

        Assert.Equal(new DesktopRect(x, y, width, height), backend.MovedBounds);
    }

    [Fact]
    public void Opacity_matches_legacy_clamping_and_off_behavior()
    {
        var backend = new FakeDesktopBackend { ActiveWindow = Active, Opacity = null };
        var actions = new DesktopActionService(backend);

        actions.AdjustOpacityActive(-30);
        Assert.Equal((byte)225, backend.SetOpacityValue);

        backend.Opacity = 20;
        actions.AdjustOpacityActive(-30);
        Assert.Equal((byte)15, backend.SetOpacityValue);

        backend.Opacity = 240;
        actions.AdjustOpacityActive(30);
        Assert.Null(backend.SetOpacityValue);
    }

    [Fact]
    public void Activate_bottom_window_of_active_class_chooses_last_matching_top_level_window()
    {
        var second = new WindowHandle(20);
        var bottom = new WindowHandle(30);
        var other = new WindowHandle(40);
        var backend = new FakeDesktopBackend
        {
            ActiveWindow = Active,
            TopLevelWindows = [Active, second, other, bottom]
        };
        backend.Classes[Active] = "Editor";
        backend.Classes[second] = "Editor";
        backend.Classes[bottom] = "Editor";
        backend.Classes[other] = "Browser";
        var actions = new DesktopActionService(backend);

        actions.ActivateBottomWindowOfActiveClass();

        Assert.Equal(bottom, backend.Activated);
    }

    [Fact]
    public void Pointer_corner_uses_one_pixel_inset_from_window_bounds()
    {
        var backend = new FakeDesktopBackend
        {
            ActiveWindow = Active,
            WindowBounds = new DesktopRect(100, 200, 300, 400)
        };
        var actions = new DesktopActionService(backend);

        actions.MovePointerToActiveWindowCorner(false);
        Assert.Equal(new DesktopPoint(101, 201), backend.Pointer);

        actions.MovePointerToActiveWindowCorner(true);
        Assert.Equal(new DesktopPoint(399, 599), backend.Pointer);
    }

    [Fact]
    public void Window_group_toggles_members_and_cycles_activation()
    {
        var second = new WindowHandle(20);
        var backend = new FakeDesktopBackend { ActiveWindow = Active };
        var groups = new WindowGroupController(backend);

        groups.ToggleActiveWindow();
        backend.ActiveWindow = second;
        groups.ToggleActiveWindow();

        Assert.Equal([Active, second], groups.Windows);
        Assert.True(groups.ActivateNext());
        Assert.Equal(Active, backend.Activated);
        Assert.True(groups.ActivateNext());
        Assert.Equal(second, backend.Activated);

        backend.ActiveWindow = Active;
        groups.ToggleActiveWindow();
        Assert.Equal(2, groups.GroupNumber);
        Assert.Equal([second], groups.Windows);
    }

    [Fact]
    public void Reset_group_advances_number_and_clears_members()
    {
        var backend = new FakeDesktopBackend { ActiveWindow = Active };
        var groups = new WindowGroupController(backend);
        groups.ToggleActiveWindow();

        groups.ResetAndAdvance();

        Assert.Equal(2, groups.GroupNumber);
        Assert.Empty(groups.Windows);
        Assert.False(groups.ActivateNext());
    }

    private sealed class FakeDesktopBackend : IDesktopBackend
    {
        public WindowHandle ActiveWindow { get; set; } = Active;
        public DesktopWindowState WindowState { get; set; }
        public DesktopRect WindowBounds { get; set; } = new(0, 0, 100, 100);
        public DesktopRect WorkArea { get; set; } = new(0, 0, 1920, 1080);
        public Dictionary<WindowHandle, string> Classes { get; } = [];
        public IReadOnlyList<WindowHandle> TopLevelWindows { get; set; } = [];
        public WindowHandle? Minimized { get; private set; }
        public WindowHandle? Maximized { get; private set; }
        public WindowHandle? Restored { get; private set; }
        public DesktopRect? MovedBounds { get; private set; }
        public WindowHandle? Activated { get; private set; }
        public bool TopMost { get; set; }
        public byte? Opacity { get; set; }
        public byte? SetOpacityValue { get; private set; }
        public bool Caption { get; set; } = true;
        public DesktopPoint Pointer { get; private set; }
        public bool MouseDown { get; set; }

        public WindowHandle GetActiveWindow() => ActiveWindow;
        public DesktopWindowState GetWindowState(WindowHandle window) => WindowState;
        public DesktopRect GetWindowBounds(WindowHandle window) => WindowBounds;
        public DesktopRect GetPrimaryWorkArea() => WorkArea;
        public string? GetWindowClass(WindowHandle window) => Classes.GetValueOrDefault(window);
        public bool IsWindow(WindowHandle window) => !window.IsEmpty;
        public void Minimize(WindowHandle window) => Minimized = window;
        public void Maximize(WindowHandle window) => Maximized = window;
        public void Restore(WindowHandle window) => Restored = window;
        public void MoveResize(WindowHandle window, DesktopRect bounds) => MovedBounds = bounds;
        public void Activate(WindowHandle window) => Activated = window;
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => TopLevelWindows;
        public bool IsTopMost(WindowHandle window) => TopMost;
        public void SetTopMost(WindowHandle window, bool enabled) => TopMost = enabled;
        public byte? GetOpacity(WindowHandle window) => Opacity;
        public void SetOpacity(WindowHandle window, byte? opacity)
        {
            SetOpacityValue = opacity;
            Opacity = opacity;
        }
        public bool HasCaption(WindowHandle window) => Caption;
        public void SetCaption(WindowHandle window, bool enabled) => Caption = enabled;
        public DesktopPoint GetPointerPosition() => Pointer;
        public void MovePointer(DesktopPoint position) => Pointer = position;
        public void MovePointerBy(int deltaX, int deltaY) => Pointer = new DesktopPoint(Pointer.X + deltaX, Pointer.Y + deltaY);
        public bool IsMouseButtonDown(DesktopMouseButton button) => MouseDown;
        public void SetMouseButton(DesktopMouseButton button, bool down) => MouseDown = down;
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
