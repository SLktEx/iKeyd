using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Profiles.HotkeySkg.Runtime;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class IKeydRuntimeCompatibilityTests
{
    [Fact]
    public void CtrlEscape_TogglesLegacySuspendAndPassesNormalKeysWhileSuspended()
    {
        using var fixture = new RuntimeFixture();

        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down(WindowsKeyMap.Control));
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(WindowsKeyMap.Escape));
        Assert.True(fixture.Runtime.IsSuspended);

        fixture.Up(WindowsKeyMap.Escape);
        fixture.Up(WindowsKeyMap.Control);
        Assert.Equal(KeyboardDisposition.PassThrough, fixture.Down((ushort)'A'));
        fixture.Up((ushort)'A');

        fixture.Down(WindowsKeyMap.Control);
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down(WindowsKeyMap.Escape));
        Assert.False(fixture.Runtime.IsSuspended);
    }

    [Fact]
    public void ConsoleCtrlV_UsesLegacySystemMenuPasteSequence()
    {
        using var fixture = new RuntimeFixture();
        fixture.Desktop.WindowClass = "ConsoleWindowClass";

        fixture.Down(WindowsKeyMap.Control);
        fixture.Keyboard.Clear();

        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down((ushort)'V'));

        Assert.Equal(
            new[]
            {
                Down(WindowsKeyMap.Alt),
                Press(WindowsKeyMap.Space),
                Up(WindowsKeyMap.Alt)
            },
            fixture.Keyboard.Events);
        Assert.Equal("ep", Assert.Single(fixture.Keyboard.Text));
    }

    [Fact]
    public void MhE_OnModernWindows_SendsWinUpLikeLegacyScript()
    {
        using var fixture = new RuntimeFixture();

        fixture.Down(WindowsKeyMap.NonConvert); // M
        fixture.Down(WindowsKeyMap.Convert);    // MH
        fixture.Keyboard.Clear();

        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down((ushort)'E'));

        Assert.Equal(
            new[]
            {
                Down(WindowsKeyMap.LeftWin),
                Press(WindowsKeyMap.Up),
                Up(WindowsKeyMap.LeftWin)
            },
            fixture.Keyboard.Events);
    }

    [Fact]
    public void KmFunction_HoldsControlAcrossWholeOutputSequence()
    {
        using var fixture = new RuntimeFixture();

        fixture.Down(WindowsKeyMap.Kana);       // K toggle on
        fixture.Down(WindowsKeyMap.NonConvert); // KM
        fixture.Keyboard.Clear();

        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down((ushort)'A'));

        Assert.Empty(fixture.Keyboard.Text);
        Assert.NotEmpty(fixture.Keyboard.Events);
        Assert.Equal(Down(WindowsKeyMap.Control), fixture.Keyboard.Events[0]);
        Assert.Contains(Press(WindowsKeyMap.Left), fixture.Keyboard.Events);
        Assert.Equal(Up(WindowsKeyMap.Control), fixture.Keyboard.Events[^1]);
    }

    [Fact]
    public void SmH_PreservesLegacyRightButtonTypo()
    {
        using var fixture = new RuntimeFixture();

        fixture.Down(WindowsKeyMap.Space);      // S
        fixture.Down(WindowsKeyMap.NonConvert); // SM

        fixture.Desktop.RightButtonDown = false;
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down((ushort)'H'));
        Assert.False(fixture.Desktop.RightButtonDown);

        fixture.Up((ushort)'H');
        fixture.Desktop.RightButtonDown = true;
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down((ushort)'H'));
        Assert.False(fixture.Desktop.RightButtonDown);
    }

    [Fact]
    public void MAndMhMacroKeys_DispatchLegacyHAndYActions()
    {
        using var fixture = new RuntimeFixture();

        fixture.Down(WindowsKeyMap.NonConvert); // M
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down((ushort)'Y'));
        Assert.Equal('Y', fixture.Interactive.LastRunMacro.GetValueOrDefault());

        fixture.Up((ushort)'Y');
        fixture.Down(WindowsKeyMap.Convert); // MH
        Assert.Equal(KeyboardDisposition.Suppress, fixture.Down((ushort)'H'));
        Assert.Equal('H', fixture.Interactive.LastEditedMacro.GetValueOrDefault());
    }

    private static string Press(ushort key) => $"press:{key:X2}";
    private static string Down(ushort key) => $"down:{key:X2}";
    private static string Up(ushort key) => $"up:{key:X2}";

    private sealed class RuntimeFixture : IDisposable
    {
        private long _timestamp;

        public RuntimeFixture()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
            var configuration = IKeydConfiguration.Load(path);
            State = new KeyboardState();
            Keyboard = new RecordingKeyboardOutput();
            Desktop = new FakeDesktopBackend();
            Interactive = new FakeInteractiveActions();
            var send = new LegacySendOutput(Keyboard, Desktop);
            Runtime = new IKeydRuntimeHandler(
                configuration,
                new FakeInputMethod(),
                State,
                send,
                Desktop,
                Interactive);
        }

        public KeyboardState State { get; }
        public RecordingKeyboardOutput Keyboard { get; }
        public FakeDesktopBackend Desktop { get; }
        public FakeInteractiveActions Interactive { get; }
        public IKeydRuntimeHandler Runtime { get; }

        public KeyboardDisposition Down(ushort virtualKey) => Dispatch(virtualKey, KeyEventKind.Down);
        public KeyboardDisposition Up(ushort virtualKey) => Dispatch(virtualKey, KeyEventKind.Up);

        public void Dispose() => Runtime.Dispose();

        private KeyboardDisposition Dispatch(ushort virtualKey, KeyEventKind kind)
        {
            var keyboardEvent = new KeyboardEvent(
                WindowsKeyMap.Keyboard(virtualKey),
                kind,
                KeyEventOrigin.Physical,
                ++_timestamp);
            State.Apply(keyboardEvent);
            return Runtime.OnKeyboardEvent(keyboardEvent);
        }
    }

    private sealed class FakeInputMethod : IInputMethod
    {
        public bool IsKanaInputActive() => false;
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<string> Events { get; } = [];
        public List<string> Text { get; } = [];

        public void Clear()
        {
            Events.Clear();
            Text.Clear();
        }

        public void SendKey(KeyboardKey key, KeyEventKind kind)
            => Events.Add($"{(kind == KeyEventKind.Down ? "down" : "up")}:{key.VirtualKey:X2}");

        public void SendKeyPress(KeyboardKey key)
            => Events.Add($"press:{key.VirtualKey:X2}");

        public void SendText(string text) => Text.Add(text);
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class FakeDesktopBackend : IDesktopBackend
    {
        private static readonly WindowHandle Active = new(1);

        public string? WindowClass { get; set; }
        public bool RightButtonDown { get; set; }
        public DesktopPoint Pointer { get; private set; }

        public WindowHandle GetActiveWindow() => Active;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(10, 20, 100, 80);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => WindowClass;
        public bool IsWindow(WindowHandle window) => !window.IsEmpty;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [Active];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => Pointer;
        public void MovePointer(DesktopPoint position) => Pointer = position;
        public void MovePointerBy(int deltaX, int deltaY) => Pointer = new(Pointer.X + deltaX, Pointer.Y + deltaY);
        public bool IsMouseButtonDown(DesktopMouseButton button)
            => button == DesktopMouseButton.Right && RightButtonDown;
        public void SetMouseButton(DesktopMouseButton button, bool down)
        {
            if (button == DesktopMouseButton.Right)
                RightButtonDown = down;
        }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }

    private sealed class FakeInteractiveActions : IHotkeySkgInteractiveActions
    {
        public char? LastRunMacro { get; private set; }
        public char? LastEditedMacro { get; private set; }
        public bool RepeatEdited { get; private set; }
        public int ClipboardHistoryShown { get; private set; }
        public int ClipboardCaptured { get; private set; }
        public int ClipboardPasted { get; private set; }

        public void RunMacro(char slot) => LastRunMacro = slot;
        public void EditMacro(char slot) => LastEditedMacro = slot;
        public void EditMacroRepeat() => RepeatEdited = true;
        public void ShowClipboardHistory() => ClipboardHistoryShown++;
        public void CaptureLatestClipboard() => ClipboardCaptured++;
        public void PasteCapturedClipboard() => ClipboardPasted++;
    }
}
