from __future__ import annotations

import json
import subprocess
from pathlib import Path

VALIDATED = "origin/issue-57-final-minimal"


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


# Avoid tooling bytecode leaking into automated commits.
gitignore = read(".gitignore")
if "__pycache__/" not in gitignore:
    if not gitignore.endswith("\n"):
        gitignore += "\n"
    gitignore += "__pycache__/\n*.pyc\n"
    write(".gitignore", gitignore)

# ---------------------------------------------------------------------------
# Explicit AHK vk+sc identity: preserve both identifiers through SendInput.
# Generic scan-code injection intentionally remains unchanged.
# ---------------------------------------------------------------------------
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

# ---------------------------------------------------------------------------
# SM exact legacy behavior: quarter-screen movement is axis-aware; the pinned
# source typo makes H release-only for the right mouse hold state.
# ---------------------------------------------------------------------------
runtime = "src/iKeyd.App/IKeydRuntimeHandler.cs"
text = read(runtime)
if "private void MoveMouse(int xDirection, int yDirection)" not in text:
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
    text = text.replace(old_block, new_block, 1)
    write(runtime, text)

# Extend only the deterministic fake backend so the release-only typo can be
# tested from a pre-held physical mouse state without touching the real pointer.
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
        raise SystemExit("runtime scenario runner mouse-state patch did not apply")
    write(runner, r)

copy_validated("tests/iKeyd.Compatibility.Tests/Scenarios/runtime-mouse-right-hold-toggle-sm-h.json")
write(
    "tests/iKeyd.Windows.Tests/Issue57RemainingCompatibilityTests.cs",
    '''using iKeyd.Compatibility.Tests;\nusing iKeyd.Core.Desktop;\nusing Xunit;\n\nnamespace iKeyd.Windows.Tests;\n\npublic sealed class Issue57RemainingCompatibilityTests\n{\n    [Fact]\n    public async Task SM_C_vertical_movement_uses_quarter_primary_height()\n    {\n        var scenario = Scenario(\n            "issue57-sm-quarter-height-up",\n            [Down(10, "C"), Down(11, "I"), Up(12, "I"), Up(13, "C")],\n            [Action("mouse", "move-by:0,-270")]);\n\n        var result = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);\n        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, result));\n    }\n\n    [Fact]\n    public async Task SM_H_is_noop_when_right_button_is_up()\n    {\n        var scenario = Scenario(\n            "issue57-sm-right-up-noop",\n            [Down(10, "H"), Up(11, "H")],\n            []);\n\n        var result = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);\n        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, result));\n    }\n\n    [Fact]\n    public async Task SM_H_releases_an_already_held_right_button()\n    {\n        var scenario = Scenario(\n            "issue57-sm-right-release-only",\n            [Down(10, "H"), Up(11, "H")],\n            [Action("mouse", "button:right:up")]);\n\n        var result = await new IKeydRuntimeScenarioRunner(DesktopMouseButton.Right).RunAsync(scenario);\n        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, result));\n    }\n\n    private static CompatibilityScenario Scenario(\n        string id,\n        List<ScenarioInputEvent> input,\n        List<ObservedAction> actions)\n        => new()\n        {\n            Id = id,\n            InitialState = new ScenarioInitialState { Mode = "S", Ime = "off", Layers = ["S", "M"] },\n            Input = input,\n            Expected = new ScenarioExpected { Actions = actions },\n            Tags = ["issue57-deterministic"]\n        };\n\n    private static ScenarioInputEvent Down(long atMs, string key)\n        => new() { AtMs = atMs, Kind = "keyDown", Key = key };\n\n    private static ScenarioInputEvent Up(long atMs, string key)\n        => new() { AtMs = atMs, Kind = "keyUp", Key = key };\n\n    private static ObservedAction Action(string kind, string value)\n        => new() { Kind = kind, Value = value };\n}\n''',
)

# ---------------------------------------------------------------------------
# Hosted oracle hardening and the known compiled S+Kana tail race.
# ---------------------------------------------------------------------------
hosted = "tests/iKeyd.Windows.Tests/HostedTModeLegacyRunner.cs"
replace_once(
    hosted,
    '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);\n                    for (var index = 0; index < digits.Count; index++)\n                    {\n                        SendModeSelectionChord(digits[index]);\n                        if (index + 1 < digits.Count)\n                            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);\n                    }\n\n                    return;\n''',
    '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);\n                    // The process can become visible just before every legacy hotkey is ready.\n                    // Repeat the idempotent selection sequence once so one missed startup chord\n                    // cannot leak stale S/K state into a hosted differential scenario.\n                    for (var attempt = 0; attempt < 2; attempt++)\n                    {\n                        for (var index = 0; index < digits.Count; index++)\n                        {\n                            SendModeSelectionChord(digits[index]);\n                            if (index + 1 < digits.Count)\n                                await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);\n                        }\n                        if (attempt == 0)\n                            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);\n                    }\n\n                    return;\n''',
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
    raise SystemExit("Send-event KANA/Escape normalization patch did not apply")
write(send_runner, s)

hosted_tests = "tests/iKeyd.Windows.Tests/HostedLegacyDifferentialTests.cs"
h = read(hosted_tests)
if "S_Kana_keeps_iKeyd_deterministic_while_accepting_the_observed_compiled_EXE_tail_race" not in h:
    insert_at = h.index("    [Fact]\n    public void Hosted_adapter_bootstraps_inner_runner_in_S_mode_and_disables_IME_dependency()")
    special = '''    [Fact]\n    [Trait("Category", "HostedLegacyDifferentialE2E")]\n    public async Task S_Kana_keeps_iKeyd_deterministic_while_accepting_the_observed_compiled_EXE_tail_race()\n    {\n        if (!OperatingSystem.IsWindows())\n            return;\n\n        var legacyRunner = new LegacySendEventScenarioRunner();\n        if (!legacyRunner.IsAvailable)\n            return;\n\n        var stableEvents = new List<ObservedKeyEvent>\n        {\n            new() { Kind = "keyDown", Key = "VK_A2" },\n            new() { Kind = "keyDown", Key = "Escape" },\n            new() { Kind = "keyUp", Key = "Escape" },\n            new() { Kind = "keyUp", Key = "VK_A2" }\n        };\n        var scenario = new CompatibilityScenario\n        {\n            Id = "runtime-s-kana-known-compiled-tail-race",\n            InitialState = new ScenarioInitialState { Mode = "S", Ime = "off", Layers = ["S"] },\n            Input =\n            [\n                new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "KANA" },\n                new ScenarioInputEvent { AtMs = 11, Kind = "keyUp", Key = "KANA" }\n            ],\n            Expected = new ScenarioExpected { Events = stableEvents }\n        };\n\n        var iKeyd = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);\n        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, iKeyd));\n\n        var legacy = await legacyRunner.RunAsync(scenario);\n        var withSpaceTail = stableEvents.Concat([\n            new ObservedKeyEvent { Kind = "keyDown", Key = "Space" },\n            new ObservedKeyEvent { Kind = "keyUp", Key = "Space" }\n        ]).ToArray();\n\n        Assert.True(\n            EventSequenceEquals(legacy.Events, stableEvents) || EventSequenceEquals(legacy.Events, withSpaceTail),\n            $"Pinned compiled EXE produced an unrecognized S+Kana sequence: {string.Join(", ", legacy.Events.Select(item => $"{item.Kind}:{item.Key}"))}");\n    }\n\n'''
    h = h[:insert_at] + special + h[insert_at:]
    helper_at = h.index("    private static CompatibilityScenario[] LoadTagged(string tag)")
    helper = '''    private static bool EventSequenceEquals(\n        IReadOnlyList<ObservedKeyEvent> actual,\n        IReadOnlyList<ObservedKeyEvent> expected)\n    {\n        if (actual.Count != expected.Count)\n            return false;\n        for (var index = 0; index < actual.Count; index++)\n        {\n            if (!string.Equals(actual[index].Kind, expected[index].Kind, StringComparison.OrdinalIgnoreCase) ||\n                !string.Equals(actual[index].Key, expected[index].Key, StringComparison.OrdinalIgnoreCase))\n                return false;\n        }\n        return true;\n    }\n\n'''
    h = h[:helper_at] + helper + h[helper_at:]
write(hosted_tests, h)

# ---------------------------------------------------------------------------
# Reconcile #57 coverage on top of CURRENT main. Context hotkeys and Suspend are
# already implemented there; only real application/IME validation remains #59.
# ---------------------------------------------------------------------------
coverage_path = Path("tests/compatibility/coverage-rules.json")
coverage = json.loads(coverage_path.read_text(encoding="utf-8"))
found = False
for rule in coverage["rules"]:
    if rule.get("name") == "process/application-specific legacy branches are explicitly handed to compatibility-fix work":
        rule["name"] = "process/application-specific legacy branches are implemented; real application verification remains"
        rule["set"] = {
            "implementation": "implemented",
            "unit": "covered",
            "scenario": "regression",
            "exeDiff": "not-required",
            "ahkDiff": "not-required",
            "realWindows": "required",
            "intentionalDifference": "no",
        }
        rule["evidence"] = [
            "src/iKeyd.App/LegacyContextualHotkeyHandler.cs",
            "tests/iKeyd.Windows.Tests/LegacyContextualHotkeyHandlerTests.cs",
            "issue:#59",
        ]
        found = True
        break
if not found:
    raise SystemExit("process-specific #57 handoff coverage rule not found")
coverage_path.write_text(json.dumps(coverage, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

process_test = "tests/tooling/test_process_specific_handoff.py"
p = read(process_test)
p = p.replace(
    "def test_uncovered_process_specific_entry_is_explicitly_routed_to_issue_57(self):",
    "def test_process_specific_entry_is_implemented_and_routed_to_real_windows_validation(self):",
)
for old, new in [
    ('self.assertEqual("deferred:#57", feature.coverage["scenario"])', 'self.assertEqual("regression", feature.coverage["scenario"])'),
    ('self.assertEqual("deferred:#57", feature.coverage["implementation"])', 'self.assertEqual("implemented", feature.coverage["implementation"])'),
    ('self.assertEqual("deferred:#57", feature.coverage["exeDiff"])', 'self.assertEqual("not-required", feature.coverage["exeDiff"])'),
    ('self.assertEqual("deferred:#57", feature.coverage["ahkDiff"])', 'self.assertEqual("not-required", feature.coverage["ahkDiff"])'),
    ('self.assertIn("issue:#57", feature.evidence)', 'self.assertIn("issue:#59", feature.evidence)'),
]:
    if old not in p:
        raise SystemExit(f"process-specific tooling assertion missing: {old}")
    p = p.replace(old, new, 1)
write(process_test, p)

inventory_test = "tests/tooling/test_legacy_compatibility_inventory.py"
i = read(inventory_test)
for old, new in [
    ('self.assertEqual("deferred:#57", console_hotkey.coverage["implementation"])', 'self.assertEqual("implemented", console_hotkey.coverage["implementation"])'),
    ('self.assertEqual("deferred:#57", console_hotkey.coverage["scenario"])', 'self.assertEqual("regression", console_hotkey.coverage["scenario"])'),
]:
    if old not in i:
        raise SystemExit(f"inventory tooling assertion missing: {old}")
    i = i.replace(old, new, 1)
write(inventory_test, i)

# Remove stale #57 wording from the human inventory guide while preserving the
# generic concept of an explicit deferred handoff.
doc = "docs/compatibility-inventory.md"
d = read(doc)
d = d.replace(
    "- `deferred:#57`: the entry is deliberately handed to long-tail compatibility-fix work because implementation/oracle behavior must be resolved before a useful scenario can be claimed.",
    "- `deferred:<issue>`: the entry is deliberately handed to a follow-up issue because implementation/oracle behavior must be resolved before a useful scenario can be claimed.",
)
d = d.replace(
    "These states never promote `exeDiff` or `ahkDiff`. Those dimensions remain independent, so routing an entry to #57/#59 cannot be mistaken for compatibility success.",
    "These states never promote `exeDiff` or `ahkDiff`. Those dimensions remain independent, so routing an entry to a follow-up issue or #59 cannot be mistaken for compatibility success.",
)
d = d.replace(
    "Likewise, the remaining unlinked window/mouse branches are explicitly routed to #57. Linked runtime scenarios are applied after the broad coverage rules, so an exact scenario link upgrades `deferred:#57` to `yes` without losing the conservative default for unlinked long-tail entries.",
    "The #57 window/mouse and process-specific long tail now has deterministic regression coverage. Real pointer/window/application effects and Japanese IME behavior remain explicitly routed to #59 instead of being inferred from hosted tests.",
)
write(doc, d)

print("final #57 landing patch applied")
