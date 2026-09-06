from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RULES = ROOT / "tests" / "compatibility" / "coverage-rules.json"
TEST = ROOT / "tests" / "tooling" / "test_mouse_intentional_coverage.py"

RULE_BLOCKS = [
'''    {
      "name": "v0.4 virtual-stick intentionally replaces legacy D 30px mouse step",
      "match": {"id": "legacy-mouse-operation-1d121fa683"},
      "set": {"intentionalDifference":"yes"},
      "evidence": ["docs/legacy-behavior.md","issue:#134","issue:#59"]
    }''',
'''    {
      "name": "v0.4 virtual-stick intentionally replaces legacy E 10px mouse step",
      "match": {"id": "legacy-mouse-operation-fa6b6cd979"},
      "set": {"intentionalDifference":"yes"},
      "evidence": ["docs/legacy-behavior.md","issue:#134","issue:#59"]
    }''',
'''    {
      "name": "v0.4 virtual-stick intentionally replaces legacy C quarter-screen mouse step",
      "match": {"id": "legacy-mouse-operation-9c146057df"},
      "set": {"intentionalDifference":"yes"},
      "evidence": ["docs/legacy-behavior.md","issue:#134","issue:#59"]
    }''',
'''    {
      "name": "v0.4 virtual-stick intentionally replaces legacy default 100px mouse step",
      "match": {"id": "legacy-mouse-operation-16aa7256ad"},
      "set": {"intentionalDifference":"yes"},
      "evidence": ["docs/legacy-behavior.md","issue:#134","issue:#59"]
    }''',
'''    {
      "name": "SM+H intentionally keeps a functional right-button press instead of the legacy typo",
      "match": {"id": "legacy-mouse-operation-485297b3c3"},
      "set": {"intentionalDifference":"yes"},
      "evidence": ["docs/legacy-behavior.md","issue:#134","issue:#59"]
    }''',
]

IDS = [
    "legacy-mouse-operation-1d121fa683",
    "legacy-mouse-operation-fa6b6cd979",
    "legacy-mouse-operation-9c146057df",
    "legacy-mouse-operation-16aa7256ad",
    "legacy-mouse-operation-485297b3c3",
]


def main() -> None:
    text = RULES.read_text(encoding="utf-8")
    missing = [block for feature_id, block in zip(IDS, RULE_BLOCKS) if feature_id not in text]
    if missing:
        marker = "\n  ]\n}"
        if marker not in text:
            raise SystemExit("coverage-rules.json closing marker not found")
        head, tail = text.rsplit(marker, 1)
        if not head.rstrip().endswith("}"):
            raise SystemExit("unexpected coverage-rules.json ending")
        text = head + ",\n" + ",\n".join(missing) + marker + tail
        RULES.write_text(text, encoding="utf-8")

    TEST.write_text('''from __future__ import annotations

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
''', encoding="utf-8")

if __name__ == "__main__":
    main()
