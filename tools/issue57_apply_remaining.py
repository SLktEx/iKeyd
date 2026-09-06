from __future__ import annotations

import subprocess
from pathlib import Path

OLD = "origin/issue-46-hotkeyskg-full-compat"


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:100]!r}")
    write(path, text.replace(old, new, 1))


def copy_old(path: str) -> None:
    data = subprocess.check_output(["git", "show", f"{OLD}:{path}"])
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(data)


# Proven regression/evidence files that cover deltas still absent from current main.
for path in [
    "tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs",
    "tests/iKeyd.Windows.Tests/Issue57RuntimeCompatibilityTests.cs",
    "tests/iKeyd.Windows.Tests/WindowsKeyboardOutputNativeIdentityTests.cs",
    "tests/iKeyd.Compatibility.Tests/Scenarios/runtime-mouse-right-hold-toggle-sm-h.json",
    "tests/compatibility/coverage-rules.json",
    "tests/tooling/test_legacy_compatibility_inventory.py",
    "tests/tooling/test_process_specific_handoff.py",
]:
    copy_old(path)

# Update final coverage evidence to current main's macro-slot implementation, not the
# superseded compatibility-branch interactive action adapter.
coverage = read("tests/compatibility/coverage-rules.json")
coverage = coverage.replace(
    '"src/iKeyd.App/IKeydApplicationContext.cs","tests/iKeyd.Core.Tests/MacroExecutorTests.cs","tests/iKeyd.Core.Tests/MacroParserTests.cs","tests/iKeyd.Windows.Tests/InteractiveRuntimeCompatibilityTests.cs","issue:#59"',
    '"src/iKeyd.App/LegacyMacroSlotController.cs","tests/iKeyd.Core.Tests/MacroExecutorTests.cs","tests/iKeyd.Core.Tests/MacroParserTests.cs","tests/iKeyd.Windows.Tests/IKeydRuntimeMacroSlotTests.cs","tests/iKeyd.Windows.Tests/LegacyMacroSlotControllerTests.cs","issue:#59"',
)
write("tests/compatibility/coverage-rules.json", coverage)

# Never track tooling bytecode.
gitignore = read(".gitignore")
if "__pycache__/" not in gitignore:
    if not gitignore.endswith("\n"):
        gitignore += "\n"
    gitignore += "__pycache__/\n*.pyc\n"
    write(".gitignore", gitignore)

# AHK explicit vk+sc tokens preserve both identifiers at the real SendInput boundary.
replace_once(
    "src/iKeyd.Core/Input/KeyboardContracts.cs",
    "public readonly record struct KeyboardKey(ushort VirtualKey, ushort ScanCode, bool IsExtended = false);",
    "public readonly record struct KeyboardKey(ushort VirtualKey, ushort ScanCode, bool IsExtended = false, bool PreserveVirtualKeyWithScanCode = false);",
)
replace_once(
    "src/iKeyd.App/WindowsKeyMap.cs",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey));\n        return true;",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey), PreserveVirtualKeyWithScanCode: true);\n        return true;",
)
replace_once(
    "src/iKeyd.Windows/Input/WindowsKeyboardOutput.cs",
    "        if (scanCode != 0)\n        {\n            virtualKey = 0;\n            flags |= KeyEventScanCode;\n        }",
    "        // Generic scan-code injection asks Windows to resolve VK from the physical scan.\n        // Explicit AHK vk+sc tokens preserve both identifiers exactly.\n        if (scanCode != 0 && !key.PreserveVirtualKeyWithScanCode)\n        {\n            virtualKey = 0;\n            flags |= KeyEventScanCode;\n        }",
)
compat = "tests/iKeyd.Windows.Tests/LegacySendOutputCompatibilityTests.cs"
for old, new in [
    ("new KeyboardKey(0x1C, 0x79)", "new KeyboardKey(0x1C, 0x79, PreserveVirtualKeyWithScanCode: true)"),
    ("new KeyboardKey(0x1D, 0x7B)", "new KeyboardKey(0x1D, 0x7B, PreserveVirtualKeyWithScanCode: true)"),
    ("new KeyboardKey(0xF3, 0x29)", "new KeyboardKey(0xF3, 0x29, PreserveVirtualKeyWithScanCode: true)"),
]:
    text = read(compat)
    if new not in text:
        if text.count(old) != 1:
            raise SystemExit(f"{compat}: cannot locate {old!r}")
        write(compat, text.replace(old, new, 1))
keyboard_tests = "tests/iKeyd.Windows.Tests/WindowsKeyboardOutputTests.cs"
if "Explicit_vk_sc_output_preserves_both_identifiers_without_scan_code_mode" not in read(keyboard_tests):
    replace_once(
        keyboard_tests,
        "    [Fact]\n    public void Extended_key_up_sets_extended_and_keyup_flags()",
        "    [Fact]\n    public void Explicit_vk_sc_output_preserves_both_identifiers_without_scan_code_mode()\n    {\n        var input = WindowsKeyboardOutput.BuildKeyInput(\n            new KeyboardKey(0xF3, 0x29, PreserveVirtualKeyWithScanCode: true),\n            KeyEventKind.Down);\n\n        Assert.Equal((ushort)0xF3, input.Data.Keyboard.VirtualKey);\n        Assert.Equal((ushort)0x29, input.Data.Keyboard.ScanCode);\n        Assert.Equal(0u, input.Data.Keyboard.Flags & 0x0008u);\n        Assert.Equal(WindowsKeyboardOutput.InjectionMarker, input.Data.Keyboard.ExtraInfo);\n    }\n\n    [Fact]\n    public void Extended_key_up_sets_extended_and_keyup_flags()",
    )

# Runtime: retain latest main's new LegacyMacroSlotController/processV/window-group/single-
# instance work and only add the remaining legacy behavior deltas.
runtime = "src/iKeyd.App/IKeydRuntimeHandler.cs"
text = read(runtime)
if "using System.Runtime.InteropServices;" not in text:
    text = text.replace("using iKeyd.Core.Chords;", "using System.Runtime.InteropServices;\nusing iKeyd.Core.Chords;", 1)
text = text.replace(
    "internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable\n{\n    private readonly object _gate",
    "internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable\n{\n    private const uint WmCommand = 0x0111;\n    private readonly object _gate",
    1,
)
text = text.replace(
    "    private long _timerDueAt;\n    private bool _disposed;",
    "    private long _timerDueAt;\n    private bool _suspended;\n    private bool _disposed;",
    1,
)
text = text.replace(
    "    internal void AttachMacroSlotActions(ILegacyMacroSlotActions macroSlots)\n",
    "    internal bool IsSuspended\n    {\n        get\n        {\n            lock (_gate)\n                return _suspended;\n        }\n    }\n\n    internal void AttachMacroSlotActions(ILegacyMacroSlotActions macroSlots)\n",
    1,
)
text = text.replace(
    "            if (_disposed)\n                return KeyboardDisposition.PassThrough;\n\n            if (TryHandleLayerKey(keyboardEvent))",
    "            if (_disposed)\n                return KeyboardDisposition.PassThrough;\n\n            if (TryHandleSuspendToggle(keyboardEvent))\n                return KeyboardDisposition.Suppress;\n\n            if (_suspended)\n            {\n                if (keyboardEvent.Kind == KeyEventKind.Up &&\n                    _suppressedKeys.Remove(keyboardEvent.Key.VirtualKey))\n                    return KeyboardDisposition.Suppress;\n                return KeyboardDisposition.PassThrough;\n            }\n\n            if (TryHandleContextHotkey(keyboardEvent))\n                return KeyboardDisposition.Suppress;\n\n            if (TryHandleLayerKey(keyboardEvent))",
    1,
)
anchor = "    private bool TryHandleLayerKey(KeyboardEvent keyboardEvent)\n"
methods = '''    private bool TryHandleSuspendToggle(KeyboardEvent keyboardEvent)\n    {\n        if (keyboardEvent.Key.VirtualKey != WindowsKeyMap.Escape ||\n            keyboardEvent.Kind != KeyEventKind.Down ||\n            !_keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control) ||\n            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt) ||\n            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Shift) ||\n            _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.LeftWin) ||\n            _keyboardState.IsVirtualKeyPressed(0x5C))\n            return false;\n\n        FlushAllPending();\n        _suspended = !_suspended;\n        _suppressedKeys.Add(WindowsKeyMap.Escape);\n        return true;\n    }\n\n    private bool TryHandleContextHotkey(KeyboardEvent keyboardEvent)\n    {\n        if (keyboardEvent.Kind != KeyEventKind.Down)\n            return false;\n\n        var window = _desktop.GetActiveWindow();\n        if (!_desktop.IsWindow(window))\n            return false;\n\n        var className = _desktop.GetWindowClass(window);\n        if (string.IsNullOrEmpty(className))\n            return false;\n\n        var ctrl = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control);\n        var alt = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt);\n        var shift = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Shift);\n        var win = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.LeftWin) ||\n                  _keyboardState.IsVirtualKeyPressed(0x5C);\n\n        if (string.Equals(className, "ConsoleWindowClass", StringComparison.Ordinal) &&\n            ctrl && !alt && !shift && !win)\n        {\n            if (keyboardEvent.Key.VirtualKey == (ushort)'V')\n            {\n                FlushAllPending();\n                _send.Send("!{Space}ep");\n                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n                return true;\n            }\n            if (keyboardEvent.Key.VirtualKey == (ushort)'X')\n            {\n                FlushAllPending();\n                _send.Send("!{Space}ek");\n                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n                return true;\n            }\n        }\n\n        if (string.Equals(className, "gsview_class", StringComparison.Ordinal) &&\n            alt && !ctrl && !shift && !win &&\n            keyboardEvent.Key.VirtualKey == (ushort)'E')\n        {\n            FlushAllPending();\n            NativeMethods.PostMessageW(window.Value, WmCommand, 105, 0);\n            _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n            return true;\n        }\n\n        return false;\n    }\n\n'''
if "private bool TryHandleSuspendToggle" not in text:
    if text.count(anchor) != 1:
        raise SystemExit("runtime: layer handler anchor mismatch")
    text = text.replace(anchor, methods + anchor, 1)

# SM exact legacy behavior.
text = text.replace("        var amount = GetMouseMoveAmount();\n        switch (key.Code)", "        switch (key.Code)", 1)
for old, new in [
    ("_desktop.MovePointerBy(-amount, 0);", "MoveMouse(-1, 0);"),
    ("_desktop.MovePointerBy(0, amount);", "MoveMouse(0, 1);"),
    ("_desktop.MovePointerBy(amount, 0);", "MoveMouse(1, 0);"),
    ("_desktop.MovePointerBy(0, -amount);", "MoveMouse(0, -1);"),
]:
    if old not in text:
        raise SystemExit(f"runtime: missing {old}")
    text = text.replace(old, new, 1)
text = text.replace(
    "            case KeyCode.H:\n                ToggleMouseButton(DesktopMouseButton.Right);\n                return true;",
    "            case KeyCode.H:\n                // Preserve the pinned legacy typo: down is unreachable from up,\n                // but an already-held right button can be released.\n                if (_desktop.IsMouseButtonDown(DesktopMouseButton.Right))\n                    _desktop.SetMouseButton(DesktopMouseButton.Right, false);\n                return true;",
    1,
)
old_amount_start = text.index("    private int GetMouseMoveAmount()\n")
old_amount_end = text.index("    private void SendLayerAction", old_amount_start)
move_block = '''    private void MoveMouse(int xDirection, int yDirection)\n    {\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))\n        {\n            _desktop.MovePointerBy(xDirection * 30, yDirection * 30);\n            return;\n        }\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))\n        {\n            _desktop.MovePointerBy(xDirection * 10, yDirection * 10);\n            return;\n        }\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))\n        {\n            var area = _desktop.GetPrimaryWorkArea();\n            _desktop.MovePointerBy(\n                xDirection * Math.Max(1, area.Width / 4),\n                yDirection * Math.Max(1, area.Height / 4));\n            return;\n        }\n        _desktop.MovePointerBy(xDirection * 100, yDirection * 100);\n    }\n\n'''
text = text[:old_amount_start] + move_block + text[old_amount_end:]
text = text.replace(
    "    private void MovePointerToActiveWindowCorner(bool bottomRight)\n    {\n        var bounds = _desktop.GetWindowBounds(_desktop.GetActiveWindow());",
    "    private void MovePointerToActiveWindowCorner(bool bottomRight)\n    {\n        var window = _desktop.GetActiveWindow();\n        if (!_desktop.IsWindow(window))\n            return;\n        var bounds = _desktop.GetWindowBounds(window);",
    1,
)
if "private static class NativeMethods" not in text:
    if not text.rstrip().endswith("}"):
        raise SystemExit("runtime: class closing brace missing")
    text = text.rstrip()[:-1] + '''\n\n    private static class NativeMethods\n    {\n        [DllImport("user32.dll", SetLastError = true)]\n        [return: MarshalAs(UnmanagedType.Bool)]\n        public static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);\n    }\n}\n'''
write(runtime, text)

# Runtime test harness extensions needed by the remaining SM regressions.
runner = "tests/iKeyd.Windows.Tests/IKeydRuntimeScenarioRunner.cs"
r = read(runner)
r = r.replace(
    "    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, \"Fixtures\", \"hotkeySKG.behavior.json\");\n\n    public string Name",
    "    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, \"Fixtures\", \"hotkeySKG.behavior.json\");\n    private readonly DesktopMouseButton[] _initialMouseButtons;\n\n    public IKeydRuntimeScenarioRunner(params DesktopMouseButton[] initialMouseButtons)\n        => _initialMouseButtons = initialMouseButtons ?? [];\n\n    public string Name",
    1,
)
r = r.replace("        var desktop = new RecordingDesktopBackend();\n        var inputMethod", "        var desktop = new RecordingDesktopBackend();\n        desktop.SetInitialMouseButtons(_initialMouseButtons);\n        var inputMethod", 1)
r = r.replace("        private readonly WindowHandle _window = new(1);\n        private readonly HashSet<DesktopMouseButton> _buttons = [];", "        private readonly WindowHandle _window = new(1);\n        private readonly WindowHandle _secondaryWindow = new(2);\n        private readonly HashSet<DesktopMouseButton> _buttons = [];", 1)
r = r.replace("        public List<ObservedAction> Actions { get; } = [];\n\n        public WindowHandle GetActiveWindow()", "        public List<ObservedAction> Actions { get; } = [];\n\n        public void SetInitialMouseButtons(IEnumerable<DesktopMouseButton> buttons)\n        {\n            _buttons.Clear();\n            foreach (var button in buttons)\n                _buttons.Add(button);\n        }\n\n        public WindowHandle GetActiveWindow()", 1)
r = r.replace("public bool IsWindow(WindowHandle window) => window == _window;", "public bool IsWindow(WindowHandle window) => window == _window || window == _secondaryWindow;", 1)
r = r.replace("public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [_window];", "public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [_window, _secondaryWindow];", 1)
write(runner, r)

# Legacy Send-event runner needs physical Kana scan input and Escape normalization.
event_runner = "tests/iKeyd.Windows.Tests/LegacySendEventScenarioRunner.cs"
e = read(event_runner)
e = e.replace(
    "        var virtualKey = ResolveVirtualKey(input.Key!);\n        SendKey(\n            virtualKey,\n            0,",
    "        var virtualKey = ResolveVirtualKey(input.Key!);\n        var scanCode = ResolveScanCode(input.Key!);\n        SendKey(\n            virtualKey,\n            scanCode,",
    1,
)
e = e.replace(
    "        return key.Trim().ToUpperInvariant() switch\n        {\n            \"COMMA\" => 0xBC,",
    "        return key.Trim().ToUpperInvariant() switch\n        {\n            \"KANA\" => VkKana,\n            \"COMMA\" => 0xBC,",
    1,
)
if "private static byte ResolveScanCode(string key)" not in e:
    marker = "    private static void SendKey(byte virtualKey, byte scanCode, bool keyUp)\n"
    e = e.replace(marker, "    private static byte ResolveScanCode(string key)\n        => key.Trim().ToUpperInvariant() switch\n        {\n            \"KANA\" => KanaScanCode,\n            _ => 0\n        };\n\n" + marker, 1)
e = e.replace('                0x12 => "Alt",\n                0x20 => "Space",', '                0x12 => "Alt",\n                0x1B => "Escape",\n                0x20 => "Space",', 1)
write(event_runner, e)

# Special S+Kana oracle: iKeyd/source stay deterministic; compiled EXE may show the
# already-observed optional Space tail race and nothing else.
hosted = "tests/iKeyd.Windows.Tests/HostedLegacyDifferentialTests.cs"
h = read(hosted)
if "S_Kana_keeps_iKeyd_deterministic_while_accepting_the_observed_compiled_EXE_tail_race" not in h:
    insert_at = h.index("    [Fact]\n    public void Hosted_adapter_bootstraps_inner_runner_in_S_mode_and_disables_IME_dependency()")
    special = '''    [Fact]\n    [Trait("Category", "HostedLegacyDifferentialE2E")]\n    public async Task S_Kana_keeps_iKeyd_deterministic_while_accepting_the_observed_compiled_EXE_tail_race()\n    {\n        if (!OperatingSystem.IsWindows()) return;\n        var legacyRunner = new LegacySendEventScenarioRunner();\n        if (!legacyRunner.IsAvailable) return;\n        var stableEvents = new List<ObservedKeyEvent>\n        {\n            new() { Kind = "keyDown", Key = "VK_A2" },\n            new() { Kind = "keyDown", Key = "Escape" },\n            new() { Kind = "keyUp", Key = "Escape" },\n            new() { Kind = "keyUp", Key = "VK_A2" }\n        };\n        var scenario = new CompatibilityScenario\n        {\n            Id = "runtime-s-kana-known-compiled-tail-race",\n            InitialState = new ScenarioInitialState { Mode = "S", Ime = "off", Layers = ["S"] },\n            Input =\n            [\n                new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "KANA" },\n                new ScenarioInputEvent { AtMs = 11, Kind = "keyUp", Key = "KANA" }\n            ],\n            Expected = new ScenarioExpected { Events = stableEvents }\n        };\n        var iKeyd = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);\n        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, iKeyd));\n        var legacy = await legacyRunner.RunAsync(scenario);\n        var withSpaceTail = stableEvents.Concat([\n            new ObservedKeyEvent { Kind = "keyDown", Key = "Space" },\n            new ObservedKeyEvent { Kind = "keyUp", Key = "Space" }\n        ]).ToArray();\n        Assert.True(\n            EventSequenceEquals(legacy.Events, stableEvents) || EventSequenceEquals(legacy.Events, withSpaceTail),\n            $"Pinned compiled EXE produced an unrecognized S+Kana sequence: {string.Join(", ", legacy.Events.Select(item => $"{item.Kind}:{item.Key}"))}");\n    }\n\n'''
    h = h[:insert_at] + special + h[insert_at:]
    helper_at = h.index("    private static CompatibilityScenario[] LoadTagged(string tag)")
    helper = '''    private static bool EventSequenceEquals(IReadOnlyList<ObservedKeyEvent> actual, IReadOnlyList<ObservedKeyEvent> expected)\n    {\n        if (actual.Count != expected.Count) return false;\n        for (var index = 0; index < actual.Count; index++)\n            if (!string.Equals(actual[index].Kind, expected[index].Kind, StringComparison.OrdinalIgnoreCase) ||\n                !string.Equals(actual[index].Key, expected[index].Key, StringComparison.OrdinalIgnoreCase)) return false;\n        return true;\n    }\n\n'''
    h = h[:helper_at] + helper + h[helper_at:]
write(hosted, h)

print("remaining #57 deltas applied to latest main")
