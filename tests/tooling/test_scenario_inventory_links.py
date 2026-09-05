import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "tools" / "validate-scenario-inventory-links.py"
spec = importlib.util.spec_from_file_location("scenario_inventory_links", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)


class ScenarioInventoryLinksTests(unittest.TestCase):
    def test_validate_reports_linked_and_unresolved_scenarios(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            scenarios = root / "Scenarios"
            scenarios.mkdir()
            (scenarios / "linked.json").write_text(json.dumps({"id": "linked"}), encoding="utf-8")
            (scenarios / "unresolved.json").write_text(json.dumps({"id": "unresolved"}), encoding="utf-8")

            matrix = root / "matrix.json"
            matrix.write_text(json.dumps({
                "source": {"sha256": "abc"},
                "features": [
                    {"id": "legacy-chord-a", "kind": "chord"},
                    {"id": "legacy-single-stroke-b", "kind": "single-stroke"},
                ],
            }), encoding="utf-8")

            links = root / "links.json"
            links.write_text(json.dumps({
                "linked": {"inventoryIds": ["legacy-chord-a"]}
            }), encoding="utf-8")

            report = module.validate(matrix, scenarios, links)
            self.assertEqual(2, report["scenarioCount"])
            self.assertEqual(1, report["linkedScenarioCount"])
            self.assertEqual(1, report["unresolvedScenarioCount"])
            self.assertEqual(["unresolved"], report["unresolvedScenarios"])
            self.assertEqual({"chord": 1}, report["linkedInventoryByKind"])

    def test_validate_rejects_unknown_inventory_ids(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            scenarios = root / "Scenarios"
            scenarios.mkdir()
            (scenarios / "scenario.json").write_text(json.dumps({"id": "scenario"}), encoding="utf-8")

            matrix = root / "matrix.json"
            matrix.write_text(json.dumps({"features": []}), encoding="utf-8")
            links = root / "links.json"
            links.write_text(json.dumps({
                "scenario": {"inventoryIds": ["legacy-missing"]}
            }), encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "unknown inventory ids"):
                module.validate(matrix, scenarios, links)

    def test_validate_rejects_links_to_unknown_scenarios(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            scenarios = root / "Scenarios"
            scenarios.mkdir()
            matrix = root / "matrix.json"
            matrix.write_text(json.dumps({"features": []}), encoding="utf-8")
            links = root / "links.json"
            links.write_text(json.dumps({
                "missing-scenario": {"inventoryIds": []}
            }), encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "unknown scenarios"):
                module.validate(matrix, scenarios, links)


if __name__ == "__main__":
    unittest.main()
