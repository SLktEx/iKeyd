from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path

OLD = "origin/issue-57-final-merge"


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")


def copy_old(path: str) -> None:
    data = subprocess.check_output(["git", "show", f"{OLD}:{path}"])
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(data)


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one anchor, got {count}: {old[:100]!r}")
    write(path, text.replace(old, new, 1))


# Keep only regression/oracle files that are still relevant on current main.
for path in [
    "tests/iKeyd.Windows.Tests/Issue57RuntimeCompatibilityTests.cs",
    "tests/iKeyd.Windows.Tests/WindowsKeyboardOutputNativeIdentityTests.cs",
    "tests/iKeyd.Windows.Tests/HostedLegacyDifferentialTests.cs",
    "tests/iKeyd.Windows.Tests/IKeydRuntimeScenarioRunner.cs",
    "tests/iKeyd.Windows.Tests/LegacySendEventScenarioRunner.cs",
    "tests/iKeyd.Windows.Tests/LegacySendOutputCompatibilityTests.cs",
    "tests/iKeyd.Windows.Tests/WindowsKeyboardOutputTests.cs",
    "tests/iKeyd.Compatibility.Tests/Scenarios/runtime-mouse-right-hold-toggle-sm-h.json",
]:
    copy_old(path)

# Context-specific tests are useful, but current main already owns Ctrl+Esc through
# LegacySuspendToggleHandler. Drop the stale runtime-local suspend test.
copy_old("tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs")
path = "tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs"
text = read(path)
text, n = re.subn(
    r'''\n    \[Fact\]\n    public void Ctrl_Escape_toggles_suspend_and_suspended_runtime_passes_normal_keys\(\)\n    \{.*?\n    \}\n\n    \[Theory\]''',
    "\n    [Theory]",
    text,
    count=1,
    flags=re.S,
)
if n != 1:
    raise SystemExit("Could not remove stale runtime-local suspend test")
write(path, text)

# Preserve explicit AHK vk+sc identity through SendInput.
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

# Current main already owns Suspend via LegacySuspendToggleHandler and macro slots via
# LegacyMacroSlotController. Add only missing app-context hotkeys and exact SM quirks.
runtime = "src/iKeyd.App/IKeydRuntimeHandler.cs"
text = read(runtime)
if "using System.Runtime.InteropServices;" not in text:
    text = text.replace("using iKeyd.Core.Chords;", "using System.Runtime.InteropServices;\nusing iKeyd.Core.Chords;", 1)
if "private const uint WmCommand = 0x0111;" not in text:
    text = text.replace(
        "internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable\n{\n",
        "internal sealed class IKeydRuntimeHandler : IKeyboardEventHandler, IMacroActionDispatcher, IDisposable\n{\n    private const uint WmCommand = 0x0111;\n",
        1,
    )
if "TryHandleContextHotkey(keyboardEvent)" not in text:
    old = "            if (_disposed)\n                return KeyboardDisposition.PassThrough;\n\n            if (TryHandleLayerKey(keyboardEvent))"
    new = "            if (_disposed)\n                return KeyboardDisposition.PassThrough;\n\n            if (TryHandleContextHotkey(keyboardEvent))\n                return KeyboardDisposition.Suppress;\n\n            if (TryHandleLayerKey(keyboardEvent))"
    if old not in text:
        raise SystemExit("runtime context-dispatch anchor not found")
    text = text.replace(old, new, 1)

if "private bool TryHandleContextHotkey" not in text:
    anchor = "    private bool TryHandleLayerKey(KeyboardEvent keyboardEvent)\n"
    method = '''    private bool TryHandleContextHotkey(KeyboardEvent keyboardEvent)\n    {\n        if (keyboardEvent.Kind != KeyEventKind.Down)\n            return false;\n\n        var window = _desktop.GetActiveWindow();\n        if (!_desktop.IsWindow(window))\n            return false;\n\n        var className = _desktop.GetWindowClass(window);\n        if (string.IsNullOrEmpty(className))\n            return false;\n\n        var ctrl = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Control);\n        var alt = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Alt);\n        var shift = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.Shift);\n        var win = _keyboardState.IsVirtualKeyPressed(WindowsKeyMap.LeftWin) ||\n                  _keyboardState.IsVirtualKeyPressed(0x5C);\n\n        if (string.Equals(className, "ConsoleWindowClass", StringComparison.Ordinal) &&\n            ctrl && !alt && !shift && !win)\n        {\n            if (keyboardEvent.Key.VirtualKey == (ushort)'V')\n            {\n                FlushAllPending();\n                _send.Send("!{Space}ep");\n                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n                return true;\n            }\n            if (keyboardEvent.Key.VirtualKey == (ushort)'X')\n            {\n                FlushAllPending();\n                _send.Send("!{Space}ek");\n                _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n                return true;\n            }\n        }\n\n        if (string.Equals(className, "gsview_class", StringComparison.Ordinal) &&\n            alt && !ctrl && !shift && !win &&\n            keyboardEvent.Key.VirtualKey == (ushort)'E')\n        {\n            FlushAllPending();\n            NativeMethods.PostMessageW(window.Value, WmCommand, 105, 0);\n            _suppressedKeys.Add(keyboardEvent.Key.VirtualKey);\n            return true;\n        }\n\n        return false;\n    }\n\n'''
    if anchor not in text:
        raise SystemExit("runtime TryHandleLayerKey anchor not found")
    text = text.replace(anchor, method + anchor, 1)

# Exact SM mouse behavior from pinned source.
if "private void MoveMouse(int xDirection, int yDirection)" not in text:
    text = text.replace("        var amount = GetMouseMoveAmount();\n        switch (key.Code)", "        switch (key.Code)", 1)
    replacements = {
        "_desktop.MovePointerBy(-amount, 0);": "MoveMouse(-1, 0);",
        "_desktop.MovePointerBy(0, amount);": "MoveMouse(0, 1);",
        "_desktop.MovePointerBy(amount, 0);": "MoveMouse(1, 0);",
        "_desktop.MovePointerBy(0, -amount);": "MoveMouse(0, -1);",
        "                ToggleMouseButton(DesktopMouseButton.Right);\n                return true;":
            "                // Preserve the pinned legacy typo: down is unreachable from up,\n                // but an already-held right button can be released.\n                if (_desktop.IsMouseButtonDown(DesktopMouseButton.Right))\n                    _desktop.SetMouseButton(DesktopMouseButton.Right, false);\n                return true;",
    }
    for old, new in replacements.items():
        if old not in text:
            raise SystemExit(f"runtime SM anchor missing: {old}")
        text = text.replace(old, new, 1)

    old_corner = '''    private void MovePointerToActiveWindowCorner(bool bottomRight)\n    {\n        var bounds = _desktop.GetWindowBounds(_desktop.GetActiveWindow());\n        if (bounds.X < 0)\n            return;\n\n        _desktop.MovePointer(bottomRight\n            ? new DesktopPoint(bounds.X + bounds.Width - 1, bounds.Y + bounds.Height - 1)\n            : new DesktopPoint(bounds.X + 1, bounds.Y + 1));\n    }\n\n    private int GetMouseMoveAmount()\n    {\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))\n            return 30;\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))\n            return 10;\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))\n            return Math.Max(1, _desktop.GetPrimaryWorkArea().Width / 4);\n        return 100;\n    }\n'''
    new_corner = '''    private void MovePointerToActiveWindowCorner(bool bottomRight)\n    {\n        var window = _desktop.GetActiveWindow();\n        if (!_desktop.IsWindow(window))\n            return;\n        var bounds = _desktop.GetWindowBounds(window);\n        if (bounds.X < 0)\n            return;\n\n        _desktop.MovePointer(bottomRight\n            ? new DesktopPoint(bounds.X + bounds.Width - 1, bounds.Y + bounds.Height - 1)\n            : new DesktopPoint(bounds.X + 1, bounds.Y + 1));\n    }\n\n    private void MoveMouse(int xDirection, int yDirection)\n    {\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'D'))\n        {\n            _desktop.MovePointerBy(xDirection * 30, yDirection * 30);\n            return;\n        }\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'E'))\n        {\n            _desktop.MovePointerBy(xDirection * 10, yDirection * 10);\n            return;\n        }\n        if (_keyboardState.IsVirtualKeyPressed((ushort)'C'))\n        {\n            var area = _desktop.GetPrimaryWorkArea();\n            _desktop.MovePointerBy(\n                xDirection * Math.Max(1, area.Width / 4),\n                yDirection * Math.Max(1, area.Height / 4));\n            return;\n        }\n        _desktop.MovePointerBy(xDirection * 100, yDirection * 100);\n    }\n'''
    if old_corner not in text:
        raise SystemExit("runtime corner/mouse amount block not found")
    text = text.replace(old_corner, new_corner, 1)

if "private static class NativeMethods" not in text:
    old = "    private void ThrowIfDisposed()\n        => ObjectDisposedException.ThrowIf(_disposed, this);\n}"
    new = '''    private void ThrowIfDisposed()\n        => ObjectDisposedException.ThrowIf(_disposed, this);\n\n    private static class NativeMethods\n    {\n        [DllImport("user32.dll", SetLastError = true)]\n        [return: MarshalAs(UnmanagedType.Bool)]\n        public static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);\n    }\n}'''
    if old not in text:
        raise SystemExit("runtime NativeMethods tail anchor not found")
    text = text.replace(old, new, 1)
write(runtime, text)

# Make hosted legacy mode selection resilient to one missed startup chord.
hosted = "tests/iKeyd.Windows.Tests/HostedTModeLegacyRunner.cs"
text = read(hosted)
old = '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);\n                    for (var index = 0; index < digits.Count; index++)\n                    {\n                        SendModeSelectionChord(digits[index]);\n                        if (index + 1 < digits.Count)\n                            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);\n                    }\n\n                    return;\n'''
new = '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);\n                    // A hosted process can become visible just before all legacy hotkeys are\n                    // ready. Repeat the idempotent mode-selection sequence once so one missed\n                    // startup chord cannot leak a stale keymap into the scenario.\n                    for (var attempt = 0; attempt < 2; attempt++)\n                    {\n                        for (var index = 0; index < digits.Count; index++)\n                        {\n                            SendModeSelectionChord(digits[index]);\n                            if (index + 1 < digits.Count)\n                                await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);\n                        }\n                        if (attempt == 0)\n                            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);\n                    }\n\n                    return;\n'''
if old not in text:
    raise SystemExit("hosted T-mode bootstrap anchor not found")
write(hosted, text.replace(old, new, 1))

# Reconcile coverage from CURRENT main instead of replacing its recent #57 rules.
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
            "src/iKeyd.App/IKeydRuntimeHandler.cs",
            "tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs",
            "issue:#59",
        ]
        found = True
        break
if not found:
    raise SystemExit("process-specific coverage handoff rule not found")
coverage_path.write_text(json.dumps(coverage, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

# Update tooling assertions to match the completed deterministic work while preserving
# all newly added main-side compatibility assertions.
path = "tests/tooling/test_process_specific_handoff.py"
text = read(path)
text = text.replace(
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
    if old not in text:
        raise SystemExit(f"process-specific tooling assertion missing: {old}")
    text = text.replace(old, new, 1)
write(path, text)

path = "tests/tooling/test_legacy_compatibility_inventory.py"
text = read(path)
for old, new in [
    ('self.assertEqual("deferred:#57", console_hotkey.coverage["implementation"])', 'self.assertEqual("implemented", console_hotkey.coverage["implementation"])'),
    ('self.assertEqual("deferred:#57", console_hotkey.coverage["scenario"])', 'self.assertEqual("regression", console_hotkey.coverage["scenario"])'),
]:
    if old not in text:
        raise SystemExit(f"inventory tooling assertion missing: {old}")
    text = text.replace(old, new, 1)
write(path, text)

print("minimal #57 compatibility patch applied on current main")
