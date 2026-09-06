from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "summarize-real-windows-verification.py"
PLAN_PATH = ROOT / "tests" / "compatibility" / "real-windows-verification-plan.json"
spec = importlib.util.spec_from_file_location("real_windows_summary", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class RealWindowsVerificationSummaryTests(unittest.TestCase):
    def setUp(self):
        self.plan = json.loads(PLAN_PATH.read_text(encoding="utf-8"))

    def test_without_report_every_manual_check_is_pending(self):
        text = module.build_summary(self.plan, None)

        expected = len(self.plan["checks"]) + len(self.plan.get("supplementalChecks", []))
        self.assertIn(f"Manual checks remaining: **{expected}**", text)
        self.assertIn("Japanese IME routing and composition", text)
        self.assertIn("Physical keyboard/hook/SendInput path", text)
        self.assertIn("Real-IME legacy differential: `not-run`", text)

    def test_passed_manual_checks_are_hidden_but_fail_skip_and_pending_remain(self):
        checks = []
        for index, check in enumerate(self.plan["checks"] + self.plan.get("supplementalChecks", [])):
            status = "pass"
            notes = ""
            if index == 1:
                status = "fail"
                notes = "vk/sc mismatch"
            elif index == 2:
                status = "skipped"
            elif index == 3:
                status = "pending"
            checks.append({
                "id": check["id"],
                "status": status,
                "notes": notes,
                "inventoryIds": check.get("inventoryIds", []),
            })

        report = {
            "automated": {
                "legacyDifferential": {"status": "pass"},
                "backendCompatibility": {"status": "pass"},
                "clipboardCompatibility": {
                    "status": "skipped",
                    "message": "protected custom clipboard",
                },
                "physicalInputCompatibility": {"status": "pass"},
            },
            "checks": checks,
            "environment": {"japaneseImeConfigured": True},
            "summary": {"complete": False},
        }

        text = module.build_summary(self.plan, report)

        self.assertNotIn("### Japanese IME routing and composition", text)
        self.assertIn("### Mode selectors and processF keyboard state", text)
        self.assertIn("Status: **fail**", text)
        self.assertIn("Existing notes: vk/sc mismatch", text)
        self.assertIn("### Application/window-context hotkeys", text)
        self.assertIn("Status: **skipped**", text)
        self.assertIn("### Macro H/Y slots, editor, repeat, calc, wait, hk and cancel", text)
        self.assertIn("Status: **pending**", text)
        self.assertIn("[x] Safe clipboard E2E: `skipped` — protected custom clipboard", text)
        self.assertIn("Manual checks remaining: **3**", text)


if __name__ == "__main__":
    unittest.main()
