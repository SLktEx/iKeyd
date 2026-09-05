#!/usr/bin/env python3
"""Overlay scenario -> inventory links onto a generated compatibility matrix.

Scenario existence upgrades only the `scenario` coverage field. It deliberately
must not claim EXE/AHK differential success; those observations are owned by the
actual differential reports.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from collections import Counter
from pathlib import Path
from typing import Any


ANALYZER_PATH = Path(__file__).with_name("analyze-legacy-compatibility.py")
_spec = importlib.util.spec_from_file_location("legacy_compatibility_analyzer", ANALYZER_PATH)
_analyzer = importlib.util.module_from_spec(_spec)
assert _spec.loader is not None
_spec.loader.exec_module(_analyzer)


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def _merge(values: list[str], extra: list[str]) -> list[str]:
    result: list[str] = []
    seen: set[str] = set()
    for value in [*values, *extra]:
        text = str(value).strip()
        key = text.casefold()
        if text and key not in seen:
            seen.add(key)
            result.append(text)
    return result


def scenario_links(scenario_dir: Path, links_path: Path) -> tuple[list[dict[str, Any]], dict[str, list[str]]]:
    sidecar = load_json(links_path)
    entries: list[dict[str, Any]] = []
    known_ids: set[str] = set()
    linked: dict[str, list[str]] = {}

    for path in sorted(scenario_dir.glob("*.json")):
        data = load_json(path)
        scenario_id = str(data.get("id", path.stem))
        if scenario_id.casefold() in known_ids:
            raise ValueError(f"duplicate scenario id: {scenario_id}")
        known_ids.add(scenario_id.casefold())

        link = sidecar.get(scenario_id, {})
        inline_ids = [str(value) for value in data.get("inventoryIds", [])]
        sidecar_ids = [str(value) for value in link.get("inventoryIds", [])]
        inventory_ids = _merge(inline_ids, sidecar_ids)
        required_environment = _merge(
            [str(value) for value in data.get("requiredEnvironment", [])],
            [str(value) for value in link.get("requiredEnvironment", [])],
        )
        oracle_targets = _merge(
            [str(value) for value in data.get("oracleTargets", [])],
            [str(value) for value in link.get("oracleTargets", [])],
        )

        if inventory_ids:
            linked[scenario_id] = inventory_ids
        entries.append({
            "path": str(path),
            "id": scenario_id,
            "tags": data.get("tags", []),
            "inventoryIds": inventory_ids,
            "requiredEnvironment": required_environment,
            "oracleTargets": oracle_targets,
        })

    unknown_scenarios = sorted(
        scenario_id for scenario_id in sidecar
        if scenario_id.casefold() not in known_ids
    )
    if unknown_scenarios:
        raise ValueError(
            "scenario inventory links reference unknown scenarios: " + ", ".join(unknown_scenarios)
        )

    return entries, linked


def apply_overlay(matrix: dict[str, Any], scenario_dir: Path, links_path: Path) -> dict[str, Any]:
    features = matrix.get("features", [])
    inventory = {str(feature.get("id")): feature for feature in features}
    entries, linked = scenario_links(scenario_dir, links_path)

    unknown_inventory: dict[str, list[str]] = {}
    for scenario_id, inventory_ids in linked.items():
        bad = [inventory_id for inventory_id in inventory_ids if inventory_id not in inventory]
        if bad:
            unknown_inventory[scenario_id] = bad
    if unknown_inventory:
        details = "; ".join(
            f"{scenario}: {', '.join(ids)}"
            for scenario, ids in sorted(unknown_inventory.items())
        )
        raise ValueError(f"scenario links contain unknown inventory ids: {details}")

    scenarios_by_inventory: dict[str, list[str]] = {}
    for scenario_id, inventory_ids in linked.items():
        for inventory_id in inventory_ids:
            scenarios_by_inventory.setdefault(inventory_id, []).append(scenario_id)

    for inventory_id, scenario_ids in scenarios_by_inventory.items():
        feature = inventory[inventory_id]
        coverage = feature.setdefault("coverage", {})
        coverage["scenario"] = "yes"
        evidence = feature.setdefault("evidence", [])
        for scenario_id in scenario_ids:
            marker = f"scenario:{scenario_id}"
            if marker not in evidence:
                evidence.append(marker)
        feature["classification"] = _analyzer.derive_classification(coverage)

    all_inventory_ids = sorted(scenarios_by_inventory)
    unresolved = sorted(
        entry["id"] for entry in entries if not entry["inventoryIds"]
    )
    matrix["scenarioIndex"] = {
        "count": len(entries),
        "files": entries,
        "inventoryIds": all_inventory_ids,
        "linkedScenarioCount": len(linked),
        "unresolvedScenarioCount": len(unresolved),
        "unresolvedScenarios": unresolved,
    }

    by_kind = Counter(str(feature.get("kind", "unknown")) for feature in features)
    by_classification = Counter(str(feature.get("classification", "unknown")) for feature in features)
    by_tag = Counter(str(tag) for feature in features for tag in feature.get("tags", []))
    summary = matrix.setdefault("summary", {})
    summary.update({
        "featureCount": len(features),
        "byKind": dict(sorted(by_kind.items())),
        "byClassification": dict(sorted(by_classification.items())),
        "byTag": dict(sorted(by_tag.items())),
        "unknownCount": sum(feature.get("classification") == "unknown" for feature in features),
        "missingCount": sum(
            feature.get("classification") in {"implementation-missing", "scenario-missing"}
            for feature in features
        ),
        "scenarioCount": len(entries),
        "linkedScenarioCount": len(linked),
        "scenarioLinkedInventoryCount": len(all_inventory_ids),
    })
    return matrix


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--scenarios", type=Path, required=True)
    parser.add_argument("--links", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    matrix = load_json(args.matrix)
    apply_overlay(matrix, args.scenarios, args.links)
    args.matrix.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(_analyzer.markdown(matrix), encoding="utf-8")
    summary = matrix["summary"]
    print(
        "scenario overlay: "
        f"{summary['linkedScenarioCount']}/{summary['scenarioCount']} scenarios linked, "
        f"{summary['scenarioLinkedInventoryCount']} inventory entries covered, "
        f"{summary['missingCount']} missing remain"
    )


if __name__ == "__main__":
    main()
