from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RULES = ROOT / "tests" / "compatibility" / "coverage-rules.json"
TEST = ROOT / "tests" / "tooling" / "test_mouse_intentional_coverage.py"

MOUSE_DIFFERENCES = [
    (
        "legacy-mouse-operation-1d121fa683",
        "v0.4 virtual-stick intentionally replaces legacy D 30px mouse step",
    ),
    (
        "legacy-mouse-operation-fa6b6cd979",
        "v0.4 virtual-stick intentionally replaces legacy E 10px mouse step",
    ),
    (
        "legacy-mouse-operation-9c146057df",
        "v0.4 virtual-stick intentionally replaces legacy C quarter-screen mouse step",
    ),
    (
        "legacy-mouse-operation-16aa7256ad",
        "v0.4 virtual-stick intentionally replaces legacy default 100px mouse step",
    ),
    (
        "legacy-mouse-operation-485297b3c3",
        "SM+H intentionally keeps a functional right-button press instead of the legacy typo",
    ),
]


def main() -> None:
    data = json.loads(RULES.read_text(encoding="utf-8"))
    rules = data["rules"]
    existing = {
        rule.get("match", {}).get("id")
        for rule in rules
        if rule.get("set", {}).get("intentionalDifference") == "yes"
    }

    for feature_id, name in MOUSE_DIFFERENCES:
        if feature_id in existing:
            continue
        rules.append(
            {
                "name": name,
                "match": {"id": feature_id},
                "set": {"intentionalDifference": "yes"},
                "evidence": ["docs/legacy-behavior.md", "issue:#134", "issue:#59"],
            }
        )

    RULES.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    TEST.write_text(
        '''from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RULES_PATH = ROOT / "tests" / "compatibility" / "coverage-rules.json"
EXPECTED_MOUSE_DIFFERENCES = {
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

    def test_only_the_five_deliberate_mouse_source_operations_are_explicitly_marked(self):
        marked = {
            rule.get("match", {}).get("id")
            for rule in self.rules
            if rule.get("set", {}).get("intentionalDifference") == "yes"
            and str(rule.get("match", {}).get("id", "")).startswith("legacy-mouse-operation-")
        }
        self.assertEqual(EXPECTED_MOUSE_DIFFERENCES, marked)

    def test_legacy_right_button_release_remains_normal_compatibility(self):
        self.assertNotIn(RIGHT_RELEASE, EXPECTED_MOUSE_DIFFERENCES)
        matching = [rule for rule in self.rules if rule.get("match", {}).get("id") == RIGHT_RELEASE]
        self.assertFalse(any(rule.get("set", {}).get("intentionalDifference") == "yes" for rule in matching))

    def test_no_broad_mouse_rule_marks_all_mouse_features_intentional(self):
        broad = [
            rule for rule in self.rules
            if rule.get("set", {}).get("intentionalDifference") == "yes"
            and rule.get("match", {}).get("tag") in ("mouse", ["mouse", "media"])
        ]
        self.assertEqual([], broad)


if __name__ == "__main__":
    unittest.main()
''',
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
