from pathlib import Path
import json


def ensure_contains(path: str, marker: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if marker in text:
        return
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: missing semantic marker {marker!r}; source matches={count}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

# main refactored WindowsKeyMap; retain only the explicit AHK vk+sc semantic delta.
ensure_contains(
    "src/iKeyd.App/WindowsKeyMap.cs",
    "PreserveVirtualKeyWithScanCode: true",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey));\n        return true;",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey), PreserveVirtualKeyWithScanCode: true);\n        return true;",
)

# main extended hosted initial-layer support. Keep it intact and ensure physical Kana
# scenario input remains available for the observed S+Kana compiled-EXE race oracle.
runner = "tests/iKeyd.Windows.Tests/LegacySendEventScenarioRunner.cs"
ensure_contains(
    runner,
    "var scanCode = ResolveScanCode(input.Key!);",
    '''    private static void SendScenarioInput(ScenarioInputEvent input)\n    {\n        var virtualKey = ResolveVirtualKey(input.Key!);\n        SendKey(\n            virtualKey,\n            0,\n            string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));\n    }''',
    '''    private static void SendScenarioInput(ScenarioInputEvent input)\n    {\n        var virtualKey = ResolveVirtualKey(input.Key!);\n        var scanCode = ResolveScanCode(input.Key!);\n        SendKey(\n            virtualKey,\n            scanCode,\n            string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));\n    }''',
)
ensure_contains(
    runner,
    '"KANA" =>',
    '''        return key.Trim().ToUpperInvariant() switch\n        {\n            "COMMA" => 0xBC,''',
    '''        return key.Trim().ToUpperInvariant() switch\n        {\n            "KANA" => VkKana,\n            "COMMA" => 0xBC,''',
)
ensure_contains(
    runner,
    "private static byte ResolveScanCode(string key)",
    '''    private static void SendKey(byte virtualKey, byte scanCode, bool keyUp)''',
    '''    private static byte ResolveScanCode(string key)\n        => key.Trim().ToUpperInvariant() switch\n        {\n            "KANA" => KanaScanCode,\n            _ => 0\n        };\n\n    private static void SendKey(byte virtualKey, byte scanCode, bool keyUp)''',
)
ensure_contains(
    runner,
    '0x1B => "Escape"',
    '''                0x12 => "Alt",\n                0x20 => "Space",''',
    '''                0x12 => "Alt",\n                0x1B => "Escape",\n                0x20 => "Space",''',
)

# main briefly modeled SM+H as a repaired right-button toggle. The pinned source and
# docs intentionally preserve the `if s tate = U` typo: while the button is up, H is
# a no-op; when already down it can release. Keep the catalog scenario honest and
# leave the pre-held release branch to Issue57RuntimeCompatibilityTests.
scenario_path = Path("tests/iKeyd.Compatibility.Tests/Scenarios/runtime-mouse-right-hold-toggle-sm-h.json")
if scenario_path.exists():
    scenario = json.loads(scenario_path.read_text(encoding="utf-8"))
    scenario["description"] = "Space then M plus H preserves the legacy typo: while right button is up, repeated H is a no-op."
    scenario.setdefault("expected", {})["actions"] = []
    scenario_path.write_text(json.dumps(scenario, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
