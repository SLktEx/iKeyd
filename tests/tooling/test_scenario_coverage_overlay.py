import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "tools" / "apply-scenario-coverage.py"
spec = importlib.util.spec_from_file_location("scenario_coverage_overlay", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)


def feature(feature_id: str, scenario: str = "missing"):
    return {
        "id": feature_id,
        "kind": "chord",
        "tags": ["keymap"],
        "coverage": {
            "implementation": "yes",
            "unit": "yes",
            "scenario": scenario,
            "exeDiff": "unverified",
            "ahkDiff": "unverified",
            "realWindows": "unverified",
            "intentionalDifference": "no",
        },
        "evidence": [],
        "classification": "scenario-missing" if scenario == "missing" else "partially-verified",
    }


class ScenarioCoverageOverlayTests(unittest.TestCase):
    def test_overlay_marks_only_scenario_coverage_and_recomputes_summary(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            scenarios = root / "Scenarios"
            scenarios.mkdir()
            (scenarios / "case.json").write_text(json.dumps({
                "id": "case",
                "tags": ["hosted-legacy"],
            }), encoding="utf-8")
            links = root / "links.json"
            links.write_text(json.dumps({
                "case": {
                    "inventoryIds": ["legacy-chord-a"],
                    "requiredEnvironment": ["hosted-windows"],
                    "oracleTargets": ["compiled-exe", "ahk-source", "ikeyd"],
                }
            }), encoding="utf-8")
            matrix = {
                "summary": {},
                "features": [feature("legacy-chord-a"), feature("legacy-chord-b")],
            }

            result = module.apply_overlay(matrix, scenarios, links)
            linked = result["features"][0]
            untouched = result["features"][1]

            self.assertEqual("yes", linked["coverage"]["scenario"])
            self.assertEqual("unverified", linked["coverage"]["exeDiff"])
            self.assertEqual("unverified", linked["coverage"]["ahkDiff"])
            self.assertEqual("partially-verified", linked["classification"])
            self.assertIn("scenario:case", linked["evidence"])
            self.assertEqual("scenario-missing", untouched["classification"])
            self.assertEqual(1, result["summary"]["missingCount"])
            self.assertEqual(1, result["summary"]["linkedScenarioCount"])
            self.assertEqual(1, result["summary"]["scenarioLinkedInventoryCount"])
            self.assertEqual([], result["scenarioIndex"]["unresolvedScenarios"])

    def test_overlay_merges_inline_and_sidecar_ids(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            scenarios = root / "Scenarios"
            scenarios.mkdir()
            (scenarios / "case.json").write_text(json.dumps({
                "id": "case",
                "inventoryIds": ["legacy-chord-a"],
            }), encoding="utf-8")
            links = root / "links.json"
            links.write_text(json.dumps({
                "case": {"inventoryIds": ["legacy-chord-b", "legacy-chord-a"]}
            }), encoding="utf-8")
            matrix = {"summary": {}, "features": [feature("legacy-chord-a"), feature("legacy-chord-b")]}

            result = module.apply_overlay(matrix, scenarios, links)
            entry = result["scenarioIndex"]["files"][0]
            self.assertEqual(["legacy-chord-a", "legacy-chord-b"], entry["inventoryIds"])
            self.assertEqual(2, result["summary"]["scenarioLinkedInventoryCount"])

    def test_overlay_rejects_unknown_inventory_id(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            scenarios = root / "Scenarios"
            scenarios.mkdir()
            (scenarios / "case.json").write_text(json.dumps({"id": "case"}), encoding="utf-8")
            links = root / "links.json"
            links.write_text(json.dumps({
                "case": {"inventoryIds": ["legacy-missing"]}
            }), encoding="utf-8")
            matrix = {"summary": {}, "features": []}

            with self.assertRaisesRegex(ValueError, "unknown inventory ids"):
                module.apply_overlay(matrix, scenarios, links)


if __name__ == "__main__":
    unittest.main()
