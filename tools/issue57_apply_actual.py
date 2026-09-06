from __future__ import annotations

import subprocess
from pathlib import Path

VALIDATED = "origin/issue-57-final-landing"


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
        raise SystemExit(f"{path}: expected one anchor, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def copy_validated(path: str) -> None:
    data = subprocess.check_output(["git", "show", f"{VALIDATED}:{path}"])
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)


# Explicit AHK vk+sc tokens preserve both identifiers through SendInput.
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
copy_validated("tests/iKeyd.Windows.Tests/WindowsKeyboardOutputNativeIdentityTests.cs")

# Exact SM semantics from the pinned legacy source.
runtime = "src/iKeyd.App/IKeydRuntimeHandler.cs"
text = read(runtime)
replace_map = {
    "        var amount = GetMouseMoveAmount();\n        switch (key.Code)": "        switch (key.Code)",
    "                _desktop.MovePointerBy(-amount, 0);": "                MoveMouse(-1, 0);",
    "                _desktop.MovePointerBy(0, amount);": "                MoveMouse(0, 1);",
    "                _desktop.MovePointerBy(amount, 0);": "                MoveMouse(1, 0);",
    "                _desktop.MovePointerBy(0, -amount);": "                MoveMouse(0, -1);",
    "            case KeyCode.H:\n                ToggleMouseButton(DesktopMouseButton.Right);\n                return true;":
        "            case KeyCode.H:\n                // Preserve the pinned legacy typo: down is unreachable from up,\n                // but an already-held right button can be released.\n                if (_desktop.IsMouseButtonDown(DesktopMouseButton.Right))\n                    _desktop.SetMouseButton(DesktopMouseButton.Right, false);\n                return true;",
}
for old, new in replace_map.items():
    if old not in text:
        raise SystemExit(f"runtime SM anchor missing: {old!r}")
    text = text.replace(old, new, 1)

old_block = '''    private void MovePointerToActiveWindowCorner(bool bottomRight)\n    {\n        var bounds = _desktop.GetWindowBounds(_desktop.GetActiveWindow());\n        if (bounds.X < 0)\n            return;\n\n        _desktop.MovePointer(bottomRight\n            ? new DesktopPoint(bounds.X + bounds.Width - 1, bounds.Y + bounds.Height - 1)\n            : new DesktopPoint(bounds.X + 1, bounds.Y + 1));\n    }\n\n    private int GetMouseMoveAmount()\n    {\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))\n            return 30;\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))\n            return 10;\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))\n            return Math.Max(1, _desktop.GetPrimaryWorkArea().Width / 4);\n        return 100;\n    }\n'''
new_block = '''    private void MovePointerToActiveWindowCorner(bool bottomRight)\n    {\n        var window = _desktop.GetActiveWindow();\n        if (!_desktop.IsWindow(window))\n            return;\n        var bounds = _desktop.GetWindowBounds(window);\n        if (bounds.X < 0)\n            return;\n\n        _desktop.MovePointer(bottomRight\n            ? new DesktopPoint(bounds.X + bounds.Width - 1, bounds.Y + bounds.Height - 1)\n            : new DesktopPoint(bounds.X + 1, bounds.Y + 1));\n    }\n\n    private void MoveMouse(int xDirection, int yDirection)\n    {\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))\n        {\n            _desktop.MovePointerBy(xDirection * 30, yDirection * 30);\n            return;\n        }\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))\n        {\n            _desktop.MovePointerBy(xDirection * 10, yDirection * 10);\n            return;\n        }\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))\n        {\n            var area = _desktop.GetPrimaryWorkArea();\n            _desktop.MovePointerBy(\n                xDirection * Math.Max(1, area.Width / 4),\n                yDirection * Math.Max(1, area.Height / 4));\n            return;\n        }\n        _desktop.MovePointerBy(xDirection * 100, yDirection * 100);\n    }\n'''
if old_block not in text:
    raise SystemExit("runtime corner/mouse block not found")
write(runtime, text.replace(old_block, new_block, 1))

runner = "tests/iKeyd.Windows.Tests/IKeydRuntimeScenarioRunner.cs"
r = read(runner)
if "_initialMouseButtons" not in r:
    r = r.replace(
        '    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");\n\n    public string Name',
        '    private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");\n    private readonly DesktopMouseButton[] _initialMouseButtons;\n\n    public IKeydRuntimeScenarioRunner(params DesktopMouseButton[] initialMouseButtons)\n        => _initialMouseButtons = initialMouseButtons ?? [];\n\n    public string Name',
        1,
    )
    r = r.replace(
        "        var desktop = new RecordingDesktopBackend();\n        var inputMethod",
        "        var desktop = new RecordingDesktopBackend();\n        desktop.SetInitialMouseButtons(_initialMouseButtons);\n        var inputMethod",
        1,
    )
    r = r.replace(
        "        public List<ObservedAction> Actions { get; } = [];\n\n        public WindowHandle GetActiveWindow()",
        "        public List<ObservedAction> Actions { get; } = [];\n\n        public void SetInitialMouseButtons(IEnumerable<DesktopMouseButton> buttons)\n        {\n            _buttons.Clear();\n            foreach (var button in buttons)\n                _buttons.Add(button);\n        }\n\n        public WindowHandle GetActiveWindow()",
        1,
    )
    if "_initialMouseButtons" not in r or "SetInitialMouseButtons" not in r:
        raise SystemExit("runtime scenario runner initial mouse state patch failed")
    write(runner, r)

copy_validated("tests/iKeyd.Compatibility.Tests/Scenarios/runtime-mouse-right-hold-toggle-sm-h.json")
copy_validated("tests/iKeyd.Windows.Tests/Issue57RemainingCompatibilityTests.cs")

# Hosted oracle hardening proven by the final landing run.
hosted = "tests/iKeyd.Windows.Tests/HostedTModeLegacyRunner.cs"
replace_once(
    hosted,
    '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);\n                    for (var index = 0; index < digits.Count; index++)\n                    {\n                        SendModeSelectionChord(digits[index]);\n                        if (index + 1 < digits.Count)\n                            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);\n                    }\n\n                    return;\n''',
    '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);\n                    // Repeat the idempotent mode-selection sequence once. The legacy\n                    // process can become visible just before every hotkey is ready.\n                    for (var attempt = 0; attempt < 2; attempt++)\n                    {\n                        for (var index = 0; index < digits.Count; index++)\n                        {\n                            SendModeSelectionChord(digits[index]);\n                            if (index + 1 < digits.Count)\n                                await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);\n                        }\n                        if (attempt == 0)\n                            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);\n                    }\n\n                    return;\n''',
)

send_runner = "tests/iKeyd.Windows.Tests/LegacySendEventScenarioRunner.cs"
s = read(send_runner)
s = s.replace(
    '''        var virtualKey = ResolveVirtualKey(input.Key!);\n        SendKey(\n            virtualKey,\n            0,\n            string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));''',
    '''        var virtualKey = ResolveVirtualKey(input.Key!);\n        var scanCode = ResolveScanCode(input.Key!);\n        SendKey(\n            virtualKey,\n            scanCode,\n            string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));''',
    1,
)
s = s.replace(
    '''        return key.Trim().ToUpperInvariant() switch\n        {\n            "COMMA" => 0xBC,''',
    '''        return key.Trim().ToUpperInvariant() switch\n        {\n            "KANA" => VkKana,\n            "COMMA" => 0xBC,''',
    1,
)
if "private static byte ResolveScanCode(string key)" not in s:
    marker = "    private static void SendKey(byte virtualKey, byte scanCode, bool keyUp)\n"
    if marker not in s:
        raise SystemExit("Send-event runner SendKey anchor not found")
    s = s.replace(
        marker,
        '''    private static byte ResolveScanCode(string key)\n        => key.Trim().ToUpperInvariant() switch\n        {\n            "KANA" => KanaScanCode,\n            _ => 0\n        };\n\n''' + marker,
        1,
    )
s = s.replace(
    '                0x12 => "Alt",\n                0x20 => "Space",',
    '                0x12 => "Alt",\n                0x1B => "Escape",\n                0x20 => "Space",',
    1,
)
if "ResolveScanCode" not in s or '"KANA" => VkKana' not in s or '0x1B => "Escape"' not in s:
    raise SystemExit("Send-event KANA/Escape normalization patch failed")
write(send_runner, s)

# The pinned compiled EXE is nondeterministic only for the optional trailing
# Space pair after S+Kana; iKeyd remains deterministic and source-compatible.
hosted_tests = "tests/iKeyd.Windows.Tests/HostedLegacyDifferentialTests.cs"
h = read(hosted_tests)
if "S_Kana_keeps_iKeyd_deterministic_while_accepting_the_observed_compiled_EXE_tail_race" not in h:
    insert_at = h.index("    [Fact]\n    public void Hosted_adapter_bootstraps_inner_runner_in_S_mode_and_disables_IME_dependency()")
    special = '''    [Fact]\n    [Trait("Category", "HostedLegacyDifferentialE2E")]\n    public async Task S_Kana_keeps_iKeyd_deterministic_while_accepting_the_observed_compiled_EXE_tail_race()\n    {\n        if (!OperatingSystem.IsWindows()) return;\n        var legacyRunner = new LegacySendEventScenarioRunner();\n        if (!legacyRunner.IsAvailable) return;\n        var stableEvents = new List<ObservedKeyEvent>\n        {\n            new() { Kind = "keyDown", Key = "VK_A2" },\n            new() { Kind = "keyDown", Key = "Escape" },\n            new() { Kind = "keyUp", Key = "Escape" },\n            new() { Kind = "keyUp", Key = "VK_A2" }\n        };\n        var scenario = new CompatibilityScenario\n        {\n            Id = "runtime-s-kana-known-compiled-tail-race",\n            InitialState = new ScenarioInitialState { Mode = "S", Ime = "off", Layers = ["S"] },\n            Input =\n            [\n                new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "KANA" },\n                new ScenarioInputEvent { AtMs = 11, Kind = "keyUp", Key = "KANA" }\n            ],\n            Expected = new ScenarioExpected { Events = stableEvents }\n        };\n        var iKeyd = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);\n        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, iKeyd));\n        var legacy = await legacyRunner.RunAsync(scenario);\n        var withSpaceTail = stableEvents.Concat([\n            new ObservedKeyEvent { Kind = "keyDown", Key = "Space" },\n            new ObservedKeyEvent { Kind = "keyUp", Key = "Space" }\n        ]).ToArray();\n        Assert.True(\n            EventSequenceEquals(legacy.Events, stableEvents) || EventSequenceEquals(legacy.Events, withSpaceTail),\n            $"Pinned compiled EXE produced an unrecognized S+Kana sequence: {string.Join(", ", legacy.Events.Select(item => $"{item.Kind}:{item.Key}"))}");\n    }\n\n'''
    h = h[:insert_at] + special + h[insert_at:]
    helper_at = h.index("    private static CompatibilityScenario[] LoadTagged(string tag)")
    helper = '''    private static bool EventSequenceEquals(IReadOnlyList<ObservedKeyEvent> actual, IReadOnlyList<ObservedKeyEvent> expected)\n    {\n        if (actual.Count != expected.Count) return false;\n        for (var index = 0; index < actual.Count; index++)\n            if (!string.Equals(actual[index].Kind, expected[index].Kind, StringComparison.OrdinalIgnoreCase) ||\n                !string.Equals(actual[index].Key, expected[index].Key, StringComparison.OrdinalIgnoreCase)) return false;\n        return true;\n    }\n\n'''
    h = h[:helper_at] + helper + h[helper_at:]
write(hosted_tests, h)

print("actual remaining compatibility gaps applied")
