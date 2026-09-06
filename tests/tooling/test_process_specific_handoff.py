from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "analyze-legacy-compatibility.py"
spec = importlib.util.spec_from_file_location("legacy_inventory_process_handoff", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class ProcessSpecificHandoffTests(unittest.TestCase):
    def rules(self):
        return json.loads(
            (ROOT / "tests" / "compatibility" / "coverage-rules.json").read_text(encoding="utf-8")
        )

    def test_implemented_console_hotkey_is_no_longer_deferred_to_issue_57(self):
        feature = module.Feature(
            "hotkey",
            1,
            '^v::Send,!{Space}ep',
            "hotkey:^v",
            "#IfWinActive ahk_class ConsoleWindowClass",
            ["send", "process-specific", "hotkey"],
        )
        module.stable_ids([feature])

        module.apply_coverage([feature], self.rules())

        self.assertEqual("implemented", feature.coverage["implementation"])
        self.assertEqual("covered", feature.coverage["unit"])
        self.assertEqual("regression", feature.coverage["scenario"])
        self.assertEqual("real-windows-verification-required", feature.classification)
        self.assertNotIn("#57", json.dumps(feature.coverage))
        self.assertIn("src/iKeyd.App/LegacyContextualHotkeyHandler.cs", feature.evidence)

    def test_remaining_context_only_surface_is_explicitly_routed_to_issue_59(self):
        feature = module.Feature(
            "context",
            1,
            "#IfWinActive ahk_class MacroDialogClass",
            "global",
            "#IfWinActive ahk_class MacroDialogClass",
            ["process-specific", "context"],
        )
        module.stable_ids([feature])

        module.apply_coverage([feature], self.rules())

        self.assertEqual("partial", feature.coverage["implementation"])
        self.assertEqual("real-windows:#59", feature.coverage["scenario"])
        self.assertEqual("required", feature.coverage["realWindows"])
        self.assertEqual("real-windows-verification-required", feature.classification)
        self.assertNotIn("#57", json.dumps(feature.coverage))
        self.assertIn("issue:#59", feature.evidence)


if __name__ == "__main__":
    unittest.main()
