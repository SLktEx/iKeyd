from pathlib import Path


def ensure_replace(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected desired text or one source match, found {count}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

# main refactored WindowsKeyMap; retain only the explicit AHK vk+sc semantic delta.
ensure_replace(
    "src/iKeyd.App/WindowsKeyMap.cs",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey));\n        return true;",
    "        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey), PreserveVirtualKeyWithScanCode: true);\n        return true;",
)

# main extended hosted initial-layer support. Keep it intact and re-add the physical
# Kana scenario input needed for the observed S+Kana compiled-EXE race oracle.
runner = "tests/iKeyd.Windows.Tests/LegacySendEventScenarioRunner.cs"
ensure_replace(
    runner,
    '''    private static void SendScenarioInput(ScenarioInputEvent input)\n    {\n        var virtualKey = ResolveVirtualKey(input.Key!);\n        SendKey(\n            virtualKey,\n            0,\n            string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));\n    }''',
    '''    private static void SendScenarioInput(ScenarioInputEvent input)\n    {\n        var virtualKey = ResolveVirtualKey(input.Key!);\n        var scanCode = ResolveScanCode(input.Key!);\n        SendKey(\n            virtualKey,\n            scanCode,\n            string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));\n    }''',
)
ensure_replace(
    runner,
    '''        return key.Trim().ToUpperInvariant() switch\n        {\n            "COMMA" => 0xBC,''',
    '''        return key.Trim().ToUpperInvariant() switch\n        {\n            "KANA" => VkKana,\n            "COMMA" => 0xBC,''',
)
ensure_replace(
    runner,
    '''    private static void SendKey(byte virtualKey, byte scanCode, bool keyUp)''',
    '''    private static byte ResolveScanCode(string key)\n        => key.Trim().ToUpperInvariant() switch\n        {\n            "KANA" => KanaScanCode,\n            _ => 0\n        };\n\n    private static void SendKey(byte virtualKey, byte scanCode, bool keyUp)''',
)
ensure_replace(
    runner,
    '''                0x12 => "Alt",\n                0x20 => "Space",''',
    '''                0x12 => "Alt",\n                0x1B => "Escape",\n                0x20 => "Space",''',
)
