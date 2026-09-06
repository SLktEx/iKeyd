from pathlib import Path
import subprocess


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

# Restore the small profile-owned interaction contract.
Path("src/iKeyd.Profiles.HotkeySkg/Runtime").mkdir(parents=True, exist_ok=True)
Path("src/iKeyd.Profiles.HotkeySkg/Runtime/HotkeySkgInteractiveActions.cs").write_text(
'''namespace iKeyd.Profiles.HotkeySkg.Runtime;

public interface IHotkeySkgInteractiveActions
{
    void RunMacro(char slot);
    void EditMacro(char slot);
    void EditMacroRepeat();
    void ShowClipboardHistory();
    void CaptureLatestClipboard();
    void PasteCapturedClipboard();
}
''', encoding="utf-8")

runtime = "src/iKeyd.App/IKeydRuntimeHandler.cs"
replace_once(runtime,
    "using iKeyd.Core.Modes;\nusing iKeyd.Windows.Input;",
    "using iKeyd.Core.Modes;\nusing iKeyd.Profiles.HotkeySkg.Runtime;\nusing iKeyd.Windows.Input;")
replace_once(runtime,
    "    private readonly WindowGroupController _windowGroup;\n    private readonly ChordEngine<string> _sEngine;",
    "    private readonly WindowGroupController _windowGroup;\n    private readonly IHotkeySkgInteractiveActions? _interactiveActions;\n    private readonly ChordEngine<string> _sEngine;")
replace_once(runtime,
    '''        LegacySendOutput send,\n        IDesktopBackend desktop)''',
    '''        LegacySendOutput send,\n        IDesktopBackend desktop,\n        IHotkeySkgInteractiveActions? interactiveActions = null)''')
replace_once(runtime,
    "        _windowGroup = new WindowGroupController(desktop);\n        _sEngine = new ChordEngine<string>(configuration.SKeymap, configuration.ChordWindowMs);",
    "        _windowGroup = new WindowGroupController(desktop);\n        _interactiveActions = interactiveActions;\n        _sEngine = new ChordEngine<string>(configuration.SKeymap, configuration.ChordWindowMs);")

# Physical typed layer dispatch: preserve MH vs HM order via LayerState.IsExact.
replace_once(runtime,
    '''            case KeyCode.G:\n                if (state.IsExact(LayerKey.M)) { _windowGroup.ActivateNext(); return true; }''',
    '''            case KeyCode.Y:\n                return DispatchMacroKey('Y', state);\n\n            case KeyCode.H:\n                return DispatchMacroKey('H', state);\n\n            case KeyCode.V:\n                return DispatchClipboardKey(state);\n\n            case KeyCode.G:\n                if (state.IsExact(LayerKey.M)) { _windowGroup.ActivateNext(); return true; }''')

# Macro-executor string-state dispatch must expose the same legacy actions.
replace_once(runtime,
    '''        if (name == "G")\n        {''',
    '''        if (name is "Y" or "H")\n            return DispatchMacroKey(name[0], state);\n\n        if (name == "V")\n            return DispatchClipboardKey(state);\n\n        if (name == "G")\n        {''')

# Add order-preserving helpers before the mouse/media handler.
replace_once(runtime,
    '''    private bool DispatchMouseMedia(KeyId key)\n    {''',
    '''    private bool DispatchMacroKey(char slot, LayerState state)\n    {\n        if (state.IsExact(LayerKey.M))\n        {\n            _interactiveActions?.RunMacro(slot);\n            return true;\n        }\n        if (state.IsExact(LayerKey.M, LayerKey.H))\n        {\n            _interactiveActions?.EditMacro(slot);\n            return true;\n        }\n        if (state.IsExact(LayerKey.H, LayerKey.M))\n        {\n            _interactiveActions?.EditMacroRepeat();\n            return true;\n        }\n        return false;\n    }\n\n    private bool DispatchMacroKey(char slot, string state)\n    {\n        if (state == "M") { _interactiveActions?.RunMacro(slot); return true; }\n        if (state == "MH") { _interactiveActions?.EditMacro(slot); return true; }\n        if (state == "HM") { _interactiveActions?.EditMacroRepeat(); return true; }\n        return false;\n    }\n\n    private bool DispatchClipboardKey(LayerState state)\n    {\n        if (state.IsExact(LayerKey.M)) { _interactiveActions?.ShowClipboardHistory(); return true; }\n        if (state.IsExact(LayerKey.M, LayerKey.H)) { _interactiveActions?.CaptureLatestClipboard(); return true; }\n        if (state.IsExact(LayerKey.H, LayerKey.M)) { _interactiveActions?.PasteCapturedClipboard(); return true; }\n        return false;\n    }\n\n    private bool DispatchClipboardKey(string state)\n    {\n        if (state == "M") { _interactiveActions?.ShowClipboardHistory(); return true; }\n        if (state == "MH") { _interactiveActions?.CaptureLatestClipboard(); return true; }\n        if (state == "HM") { _interactiveActions?.PasteCapturedClipboard(); return true; }\n        return false;\n    }\n\n    private bool DispatchMouseMedia(KeyId key)\n    {''')

# Use the previously validated full interactive App implementation as a base, then
# marshal dialog/clipboard operations back to the WinForms UI thread.
old_app = subprocess.check_output([
    "git", "show",
    "origin/archive/issue-46-pre-main-sync-20260906:src/iKeyd.App/IKeydApplicationContext.cs"
], text=True, encoding="utf-8")
old_app = old_app.replace(
    '''    public void EditMacro(char slot)\n    {\n        slot = NormalizeMacroSlot(slot);\n        _ = Task.Run(() => EditLegacyMacroCore(slot));\n    }\n\n    public void EditMacroRepeat()\n        => _ = Task.Run(EditMacroRepeatCore);\n\n    public void ShowClipboardHistory()\n        => _ = Task.Run(() => RunInteractiveAction(ShowClipboardHistoryCore, "Clipboard history failed."));\n\n    public void CaptureLatestClipboard()\n        => _ = Task.Run(() => RunInteractiveAction(() => _clipboard.CaptureLatest(), "Could not capture clipboard history item."));\n\n    public void PasteCapturedClipboard()\n        => _ = Task.Run(() => RunInteractiveAction(() => _clipboard.PasteCaptured(), "Could not paste captured clipboard history item."));''',
    '''    public void EditMacro(char slot)\n    {\n        slot = NormalizeMacroSlot(slot);\n        PostToUi(() => EditLegacyMacroCore(slot));\n    }\n\n    public void EditMacroRepeat()\n        => PostToUi(EditMacroRepeatCore);\n\n    public void ShowClipboardHistory()\n        => PostToUi(() => RunInteractiveAction(ShowClipboardHistoryCore, "Clipboard history failed."));\n\n    public void CaptureLatestClipboard()\n        => PostToUi(() => RunInteractiveAction(() => _clipboard.CaptureLatest(), "Could not capture clipboard history item."));\n\n    public void PasteCapturedClipboard()\n        => PostToUi(() => RunInteractiveAction(() => _clipboard.PasteCaptured(), "Could not paste captured clipboard history item."));''')
old_app = old_app.replace(
    '''    private void RunInteractiveAction(Action action, string errorMessage)\n    {''',
    '''    private void PostToUi(Action action)\n        => _uiContext.Post(_ =>\n        {\n            if (!_stopping)\n                action();\n        }, null);\n\n    private void RunInteractiveAction(Action action, string errorMessage)\n    {''')
Path("src/iKeyd.App/IKeydApplicationContext.cs").write_text(old_app, encoding="utf-8")

Path("tests/iKeyd.Windows.Tests/InteractiveRuntimeCompatibilityTests.cs").write_text(r'''using iKeyd.App;
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

    [Theory]
    [InlineData("M", "clipboard-show")]
    [InlineData("MH", "clipboard-capture")]
    [InlineData("HM", "clipboard-paste")]
    public async Task Clipboard_hotkey_states_dispatch_to_legacy_interactive_actions(string state, string expected)
    {
        var interactive = new RecordingInteractiveActions();
        using var runtime = CreateRuntime(interactive);

        await runtime.DispatchAsync(new MacroHotkey(state, 'V'), CancellationToken.None);

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
            interactive);
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
''', encoding="utf-8")
