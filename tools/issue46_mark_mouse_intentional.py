from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RULES = ROOT / "tests" / "compatibility" / "coverage-rules.json"
TEST = ROOT / "tests" / "tooling" / "test_mouse_intentional_coverage.py"

MOUSE_DIFFERENCES = [
    ("legacy-mouse-operation-1d121fa683", "v0.4 virtual-stick intentionally replaces legacy D 30px mouse step"),
    ("legacy-mouse-operation-fa6b6cd979", "v0.4 virtual-stick intentionally replaces legacy E 10px mouse step"),
    ("legacy-mouse-operation-9c146057df", "v0.4 virtual-stick intentionally replaces legacy C quarter-screen mouse step"),
    ("legacy-mouse-operation-16aa7256ad", "v0.4 virtual-stick intentionally replaces legacy default 100px mouse step"),
    ("legacy-mouse-operation-485297b3c3", "SM+H intentionally keeps a functional right-button press instead of the legacy typo"),
]

def main() -> None:
    data = json.loads(RULES.read_text(encoding="utf-8"))
    rules = data["rules"]
    existing = {r.get("match", {}).get("id") for r in rules if r.get("set", {}).get("intentionalDifference") == "yes"}
    for feature_id, name in MOUSE_DIFFERENCES:
        if feature_id not in existing:
            rules.append({"name": name, "match": {"id": feature_id}, "set": {"intentionalDifference": "yes"}, "evidence": ["docs/legacy-behavior.md", "issue:#134", "issue:#59"]})
    RULES.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    TEST.write_text('''from __future__ import annotations\n\nimport json\nimport unittest\nfrom pathlib import Path\n\nROOT = Path(__file__).resolve().parents[2]\nRULES_PATH = ROOT / "tests" / "compatibility" / "coverage-rules.json"\nEXPECTED = {\n    "legacy-mouse-operation-1d121fa683",\n    "legacy-mouse-operation-fa6b6cd979",\n    "legacy-mouse-operation-9c146057df",\n    "legacy-mouse-operation-16aa7256ad",\n    "legacy-mouse-operation-485297b3c3",\n}\nRIGHT_RELEASE = "legacy-mouse-operation-9f3f14c1e1"\n\nclass MouseIntentionalCoverageTests(unittest.TestCase):\n    def setUp(self):\n        self.rules = json.loads(RULES_PATH.read_text(encoding="utf-8"))["rules"]\n\n    def test_exact_mouse_intentional_set(self):\n        marked = {r.get("match", {}).get("id") for r in self.rules if r.get("set", {}).get("intentionalDifference") == "yes" and str(r.get("match", {}).get("id", "")).startswith("legacy-mouse-operation-")}\n        self.assertEqual(EXPECTED, marked)\n\n    def test_right_release_not_intentional(self):\n        self.assertFalse(any(r.get("match", {}).get("id") == RIGHT_RELEASE and r.get("set", {}).get("intentionalDifference") == "yes" for r in self.rules))\n\n    def test_total_intentional_rules_include_two_existing_send_false_positives(self):\n        marked = {r.get("match", {}).get("id") for r in self.rules if r.get("set", {}).get("intentionalDifference") == "yes"}\n        self.assertIn("legacy-send-19d656068e", marked)\n        self.assertIn("legacy-send-c963e4ab3e", marked)\n        self.assertEqual(7, len(marked))\n\nif __name__ == "__main__":\n    unittest.main()\n''', encoding="utf-8")

if __name__ == "__main__":
    main()
