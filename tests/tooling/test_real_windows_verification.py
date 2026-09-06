from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from copy import deepcopy
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "validate-real-windows-verification.py"
PLAN_PATH = ROOT / "tests" / "compatibility" / "real-windows-verification-plan.json"
spec = importlib.util.spec_from_file_location("real_windows_verification", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class RealWindowsVerificationTests(unittest.TestCase):
    def setUp(self):
        self.plan = json.loads(PLAN_PATH.read_text(encoding="utf-8"))

    def complete_report(self):
        checks = []
        for check in self.plan["checks"] + self.plan.get("supplementalChecks", []):
            checks.append({
                "id": check["id"],
                "title": check["title"],
                "mode": check.get("mode", "manual"),
                "status": "pass",
                "notes": "verified",
                "inventoryIds": check.get("inventoryIds", []),
            })
        return {
            "schemaVersion": 1,
            "planId": self.plan["planId"],
            "issue": 59,
            "binaries": {
                "legacy": {"sha256": self.plan["pinnedLegacyExeSha256"]},
                "ikeyd": {"sha256": "a" * 64},
            },
            "environment": {"japaneseImeConfigured": True},
            "automated": {"legacyDifferential": {"status": "pass"}},
            "checks": checks,
            "summary": {
                "expectedRealWindowsInventoryCount": self.plan["expectedRealWindowsInventoryCount"],
                "plannedInventoryCount": self.plan["expectedRealWindowsInventoryCount"],
                "complete": True,
            },
        }

    def test_plan_pins_all_162_real_windows_inventory_entries_once(self):
        self.assertEqual([], module.validate_plan(self.plan))
        ids = [item for check in self.plan["checks"] for item in check["inventoryIds"]]
        self.assertEqual(162, len(ids))
        self.assertEqual(162, len(set(ids)))
        self.assertEqual(
            {
                "ime-routing": 9,
                "mode-function": 6,
                "contextual-apps": 13,
                "macro-slots": 40,
                "clipboard-ui": 34,
                "window-desktop": 28,
                "mouse-media": 32,
            },
            {check["id"]: len(check["inventoryIds"]) for check in self.plan["checks"]},
        )

    def test_complete_report_requires_every_pinned_check_and_identity(self):
        report = self.complete_report()
        self.assertEqual([], module.validate_report(self.plan, report, require_complete=True))

        incomplete = deepcopy(report)
        incomplete["checks"][0]["status"] = "pending"
        incomplete["summary"]["complete"] = False
        errors = module.validate_report(self.plan, incomplete, require_complete=True)
        self.assertTrue(any("checks are not complete" in error for error in errors))

    def test_report_rejects_inventory_drift_and_wrong_legacy_binary(self):
        report = self.complete_report()
        report["checks"][0]["inventoryIds"] = report["checks"][0]["inventoryIds"][:-1]
        report["binaries"]["legacy"]["sha256"] = "0" * 64
        errors = module.validate_report(self.plan, report, require_complete=False)
        self.assertTrue(any("inventoryIds do not match" in error for error in errors))
        self.assertTrue(any("Legacy" in error or "legacy" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
