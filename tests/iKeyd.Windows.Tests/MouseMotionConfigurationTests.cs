using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class MouseMotionConfigurationTests
{
    [Fact]
    public void Windows_configuration_projects_mouse_json()
    {
        const string json = """
        {
          "source": { "chordWindowMs": 40 },
          "singleStroke": { "S": {}, "K": {} },
          "chords": { "S": [], "K": [] },
          "mouse": {
            "updateMs": 4,
            "response": { "pressMs": 25, "releaseMs": 1, "curve": "linear" },
            "speed": { "normal": 1500, "precision": 400, "fine": 90, "fast": 3200 },
            "tapNudgePixels": 3,
            "maxCatchupMs": 20
          }
        }
        """;

        var configuration = IKeydConfiguration.Parse(json);

        Assert.Equal(4, configuration.Mouse.UpdateIntervalMs);
        Assert.Equal(25, configuration.Mouse.PressMs);
        Assert.Equal(1, configuration.Mouse.ReleaseMs);
        Assert.Equal("linear", configuration.Mouse.Curve);
        Assert.Equal(1500, configuration.Mouse.NormalSpeed);
        Assert.Equal(3, configuration.Mouse.TapNudgePixels);
        Assert.Equal(20, configuration.Mouse.MaxCatchupMs);
        Assert.Equal(400, KeyboardMouseMotion.SpeedForModifiers(configuration.Mouse, precision: true, fine: false, fast: false));
        Assert.Equal(90, KeyboardMouseMotion.SpeedForModifiers(configuration.Mouse, precision: false, fine: true, fast: false));
        Assert.Equal(3200, KeyboardMouseMotion.SpeedForModifiers(configuration.Mouse, precision: false, fine: false, fast: true));
    }

    [Fact]
    public void Explicit_profile_controls_motion_controller_tap_nudge()
    {
        var custom = new MouseMotionProfile(
            "virtual_stick",
            1000,
            45,
            2,
            "smoothstep",
            1500,
            400,
            90,
            3200,
            "neutral",
            3,
            32);
        var desktop = new RecordingDesktopBackend();
        var keyboard = new KeyboardState();

        using var motion = new KeyboardMouseMotion(desktop, keyboard, custom);

        Assert.True(motion.TryStart(new KeyId(KeyCode.J), (ushort)'J'));
        Assert.Equal([new PointerMotionDelta(-3, 0)], desktop.Moves);
        Assert.True(motion.TryRelease((ushort)'J'));
    }

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        private readonly WindowHandle _window = new(1);
        public List<PointerMotionDelta> Moves { get; } = [];

        public WindowHandle GetActiveWindow() => _window;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "MouseMotionConfigurationTest";
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
        public void MovePointerBy(int deltaX, int deltaY) => Moves.Add(new PointerMotionDelta(deltaX, deltaY));
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
