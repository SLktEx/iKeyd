using iKeyd.App;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class BehaviorPcActionRouterTests
{
    [Fact]
    public void MO_activates_layer_immediately_for_next_key()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var desktop = new RecordingDesktopBackend();
        var profile = new AutomationProfile(40,
        [
            new AutomationKeymapProfile("S", [], [],
            [
                new BehaviorMappingProfile("H", new BehaviorInvocationProfile("MO", ["NAV"]))
            ]),
            new AutomationKeymapProfile("K", [], []),
            new AutomationKeymapProfile("NAV", [new SingleMapping<string>("B", "nav-b")], [])
        ]);

        using var router = new BehaviorWindowsInputRouter(
            profile, () => "S", new LegacySendOutput(keyboard), keyboard, fallback, desktop);

        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('H', KeyEventKind.Down, 0)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Down, 10)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('B', KeyEventKind.Up, 11)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical('H', KeyEventKind.Up, 20)));

        Assert.Equal(["nav-b"], keyboard.Text);
        Assert.Empty(fallback.Events);
    }

    [Fact]
    public void PC_actions_dispatch_to_desktop_keyboard_and_host_capabilities()
    {
        var keyboard = new RecordingKeyboardOutput();
        var fallback = new RecordingHandler();
        var desktop = new RecordingDesktopBackend();
        var host = new RecordingHostSink();
        var profile = new AutomationProfile(40,
        [
            new AutomationKeymapProfile("S", [], [],
            [
                Binding("H", "MOUSE_MOVE", "-30", "10"),
                Binding("J", "MOUSE_CLICK", "Left"),
                Binding("K", "SCROLL", "Down"),
                Binding("L", "MEDIA", "PlayPause"),
                Binding("U", "WINDOW", "LeftHalf"),
                Binding("I", "TEXT", "^+{}"),
                Binding("O", "CLIPBOARD", "History"),
                Binding("P", "MACRO", "hello, world")
            ]),
            new AutomationKeymapProfile("K", [], [])
        ]);

        using var router = new BehaviorWindowsInputRouter(
            profile, () => "S", new LegacySendOutput(keyboard), keyboard, fallback, desktop, host);

        Press(router, 'H', 10);
        Press(router, 'J', 20);
        Press(router, 'K', 30);
        Press(router, 'L', 40);
        Press(router, 'U', 50);
        Press(router, 'I', 60);
        Press(router, 'O', 70);
        Press(router, 'P', 80);

        Assert.Equal([(-30, 10)], desktop.Moves);
        Assert.Equal([DesktopMouseButton.Left], desktop.Clicks);
        Assert.Equal([-120], desktop.Scrolls);
        Assert.Equal([DesktopMediaCommand.PlayPause], desktop.Media);
        Assert.Equal(new DesktopRect(0, 0, 600, 800), desktop.Bounds);
        Assert.Equal(["^+{}"], keyboard.Text);
        Assert.Equal(
            [BehaviorAction.Clipboard("History"), BehaviorAction.Macro("hello, world")],
            host.Actions);
        Assert.Empty(fallback.Events);
    }

    private static BehaviorMappingProfile Binding(string key, string name, params string[] arguments)
        => new(key, new BehaviorInvocationProfile(name, arguments));

    private static void Press(BehaviorWindowsInputRouter router, ushort key, long timestamp)
    {
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical(key, KeyEventKind.Down, timestamp)));
        Assert.Equal(KeyboardDisposition.Suppress, router.OnKeyboardEvent(Physical(key, KeyEventKind.Up, timestamp + 1)));
    }

    private static KeyboardEvent Physical(ushort key, KeyEventKind kind, long timestamp)
        => new(WindowsKeyMap.Keyboard(key), kind, KeyEventOrigin.Physical, timestamp);

    private sealed class RecordingHostSink : IBehaviorHostActionSink
    {
        public List<BehaviorAction> Actions { get; } = [];
        public void Post(BehaviorAction action) => Actions.Add(action);
    }

    private sealed class RecordingHandler : IKeyboardEventHandler
    {
        public List<KeyboardEvent> Events { get; } = [];
        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            Events.Add(keyboardEvent);
            return KeyboardDisposition.PassThrough;
        }
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<string> Text { get; } = [];
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class RecordingDesktopBackend : IDesktopBackend
    {
        private static readonly WindowHandle Active = new(1);

        public List<(int X, int Y)> Moves { get; } = [];
        public List<DesktopMouseButton> Clicks { get; } = [];
        public List<int> Scrolls { get; } = [];
        public List<DesktopMediaCommand> Media { get; } = [];
        public DesktopRect Bounds { get; private set; } = new(100, 100, 800, 600);
        public DesktopWindowState WindowState { get; private set; } = DesktopWindowState.Normal;
        public bool TopMost { get; private set; }
        public bool Caption { get; private set; } = true;
        public byte? Opacity { get; private set; }

        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => WindowState;
        public DesktopRect GetWindowBounds(WindowHandle window) => Bounds;
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1200, 800);
        public string? GetWindowClass(WindowHandle window) => "Test";
        public bool IsWindow(WindowHandle window) => !window.IsEmpty;
        public void Minimize(WindowHandle window) => WindowState = DesktopWindowState.Minimized;
        public void Maximize(WindowHandle window) => WindowState = DesktopWindowState.Maximized;
        public void Restore(WindowHandle window) => WindowState = DesktopWindowState.Normal;
        public void MoveResize(WindowHandle window, DesktopRect bounds) => Bounds = bounds;
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [Active];
        public bool IsTopMost(WindowHandle window) => TopMost;
        public void SetTopMost(WindowHandle window, bool enabled) => TopMost = enabled;
        public byte? GetOpacity(WindowHandle window) => Opacity;
        public void SetOpacity(WindowHandle window, byte? opacity) => Opacity = opacity;
        public bool HasCaption(WindowHandle window) => Caption;
        public void SetCaption(WindowHandle window, bool enabled) => Caption = enabled;
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) => Moves.Add((deltaX, deltaY));
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) => Clicks.Add(button);
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) => Scrolls.Add(wheelDelta);
        public void SendMediaCommand(DesktopMediaCommand command) => Media.Add(command);
    }
}
