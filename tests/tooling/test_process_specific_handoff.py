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
    def test_process_specific_entry_is_implemented_and_routed_to_issue_59(self):
        feature = module.Feature(
            "hotkey",
            1,
            '^v::Send,!{Space}ep',
            "hotkey:^v",
            "#IfWinActive ahk_class ConsoleWindowClass",
            ["send", "process-specific", "hotkey"],
        )
        module.stable_ids([feature])
        rules = json.loads(
            (ROOT / "tests" / "compatibility" / "coverage-rules.json").read_text(encoding="utf-8")
        )

        module.apply_coverage([feature], rules)

        self.assertEqual("implemented", feature.coverage["implementation"])
        self.assertEqual("covered", feature.coverage["unit"])
        self.assertEqual("regression", feature.coverage["scenario"])
        self.assertEqual("not-required", feature.coverage["exeDiff"])
        self.assertEqual("not-required", feature.coverage["ahkDiff"])
        self.assertEqual("required", feature.coverage["realWindows"])
        self.assertEqual("no", feature.coverage["intentionalDifference"])
        self.assertEqual("real-windows-verification-required", feature.classification)
        self.assertIn("tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs", feature.evidence)
        self.assertIn("issue:#59", feature.evidence)


if __name__ == "__main__":
    unittest.main()
