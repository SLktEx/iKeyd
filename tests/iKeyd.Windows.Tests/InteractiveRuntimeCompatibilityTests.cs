using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Macros;
using iKeyd.Profiles.HotkeySkg.Runtime;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class InteractiveRuntimeCompatibilityTests
{
    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

    [Theory]
    [InlineData('Y', "M", "run:Y")]
    [InlineData('H', "M", "run:H")]
    [InlineData('Y', "MH", "edit:Y")]
    [InlineData('H', "MH", "edit:H")]
    [InlineData('Y', "HM", "edit-repeat")]
    [InlineData('H', "HM", "edit-repeat")]
    public async Task Macro_hotkey_states_dispatch_to_legacy_interactive_actions(char slot, string state, string expected)
    {
        var interactive = new RecordingInteractiveActions();
        using var runtime = CreateRuntime(interactive);

        await runtime.DispatchAsync(new MacroHotkey(state, slot), CancellationToken.None);

        Assert.Equal([expected], interactive.Actions);
    }


    [Fact]
    public void Physical_MH_and_HM_keep_distinct_macro_meanings()
    {
        var interactive = new RecordingInteractiveActions();
        var keyboardState = new KeyboardState();
        using var runtime = CreateRuntime(interactive, keyboardState);

        Dispatch(runtime, keyboardState, WindowsKeyMap.NonConvert, KeyEventKind.Down, 1);
        Dispatch(runtime, keyboardState, WindowsKeyMap.Convert, KeyEventKind.Down, 2);
        Dispatch(runtime, keyboardState, (ushort)'Y', KeyEventKind.Down, 3);
        Assert.Equal("edit:Y", Assert.Single(interactive.Actions));

        interactive.Actions.Clear();
        Dispatch(runtime, keyboardState, WindowsKeyMap.Convert, KeyEventKind.Up, 4);
        Dispatch(runtime, keyboardState, WindowsKeyMap.NonConvert, KeyEventKind.Up, 5);
        Dispatch(runtime, keyboardState, WindowsKeyMap.Convert, KeyEventKind.Down, 6);
        Dispatch(runtime, keyboardState, WindowsKeyMap.NonConvert, KeyEventKind.Down, 7);
        Dispatch(runtime, keyboardState, (ushort)'H', KeyEventKind.Down, 8);
        Assert.Equal("edit-repeat", Assert.Single(interactive.Actions));
    }

    private static IKeydRuntimeHandler CreateRuntime(RecordingInteractiveActions interactive, KeyboardState? state = null)
    {
        state ??= new KeyboardState();
        var desktop = new NullDesktopBackend();
        return new IKeydRuntimeHandler(
            IKeydConfiguration.Load(ProfilePath),
            new FixedInputMethod(),
            state,
            new LegacySendOutput(new NullKeyboardOutput(), desktop),
            desktop,
            interactiveActions: interactive);
    }

    private static void Dispatch(IKeydRuntimeHandler runtime, KeyboardState state, ushort key, KeyEventKind kind, long time)
    {
        var item = new KeyboardEvent(WindowsKeyMap.Keyboard(key), kind, KeyEventOrigin.Physical, time);
        state.Apply(item);
        runtime.OnKeyboardEvent(item);
    }

    private sealed class RecordingInteractiveActions : IHotkeySkgInteractiveActions
    {
        public List<string> Actions { get; } = [];
        public void RunMacro(char slot) => Actions.Add($"run:{slot}");
        public void EditMacro(char slot) => Actions.Add($"edit:{slot}");
        public void EditMacroRepeat() => Actions.Add("edit-repeat");
        public void ShowClipboardHistory() => Actions.Add("clipboard-show");
        public void CaptureLatestClipboard() => Actions.Add("clipboard-capture");
        public void PasteCapturedClipboard() => Actions.Add("clipboard-paste");
    }

    private sealed class FixedInputMethod : IInputMethod { public bool IsKanaInputActive() => false; }
    private sealed class NullKeyboardOutput : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }
    private sealed class NullDesktopBackend : IDesktopBackend
    {
        public WindowHandle GetActiveWindow() => default;
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => default;
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => null;
        public bool IsWindow(WindowHandle window) => false;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => default;
        public void MovePointer(DesktopPoint position) { }
        public void MovePointerBy(int deltaX, int deltaY) { }
        public bool IsMouseButtonDown(DesktopMouseButton button) => false;
        public void SetMouseButton(DesktopMouseButton button, bool down) { }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
