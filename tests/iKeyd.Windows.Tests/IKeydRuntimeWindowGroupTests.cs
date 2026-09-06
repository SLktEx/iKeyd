using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Macros;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class IKeydRuntimeWindowGroupTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Fact]
    public async Task ProcessB_M_activates_the_bottom_window_of_the_active_class()
    {
        var desktop = new TwoWindowDesktopBackend();
        var keyboard = new NullKeyboardOutput();
        using var runtime = new IKeydRuntimeHandler(
            IKeydConfiguration.Load(ProfilePath),
            new InactiveInputMethod(),
            new KeyboardState(),
            new LegacySendOutput(keyboard),
            desktop);

        await runtime.DispatchAsync(new MacroHotkey("M", 'b'), CancellationToken.None);

        Assert.Equal(desktop.Secondary, desktop.Activated);
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

    private sealed class TwoWindowDesktopBackend : IDesktopBackend
    {
        public WindowHandle Active { get; } = new(1);
        public WindowHandle Secondary { get; } = new(2);
        public WindowHandle Activated { get; private set; }

        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(100, 100, 800, 600);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window)
            => window is { Value: 1 or 2 } ? "SharedClass" : null;
        public bool IsWindow(WindowHandle window) => window == Active || window == Secondary;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) => Activated = window;
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [Active, Secondary];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => new(0, 0);
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
