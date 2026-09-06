from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RULES_PATH = ROOT / "tests" / "compatibility" / "coverage-rules.json"
EXPECTED = {
    "legacy-mouse-operation-1d121fa683",
    "legacy-mouse-operation-fa6b6cd979",
    "legacy-mouse-operation-9c146057df",
    "legacy-mouse-operation-16aa7256ad",
    "legacy-mouse-operation-485297b3c3",
}
RIGHT_RELEASE = "legacy-mouse-operation-9f3f14c1e1"

class MouseIntentionalCoverageTests(unittest.TestCase):
    def setUp(self):
        self.rules = json.loads(RULES_PATH.read_text(encoding="utf-8"))["rules"]

    def test_exact_mouse_intentional_set(self):
        marked = {r.get("match", {}).get("id") for r in self.rules if r.get("set", {}).get("intentionalDifference") == "yes" and str(r.get("match", {}).get("id", "")).startswith("legacy-mouse-operation-")}
        self.assertEqual(EXPECTED, marked)

    def test_right_release_not_intentional(self):
        self.assertFalse(any(r.get("match", {}).get("id") == RIGHT_RELEASE and r.get("set", {}).get("intentionalDifference") == "yes" for r in self.rules))

    def test_total_intentional_rule_count(self):
        marked = {r.get("match", {}).get("id") for r in self.rules if r.get("set", {}).get("intentionalDifference") == "yes"}
        self.assertEqual(7, len(marked))
        self.assertIn("legacy-send-19d656068e", marked)
        self.assertIn("legacy-send-c963e4ab3e", marked)

if __name__ == "__main__":
    unittest.main()
