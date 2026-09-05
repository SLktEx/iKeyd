#!/usr/bin/env python3
"""Validate scenario -> legacy inventory links and summarize unresolved scenarios."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def validate(matrix_path: Path, scenario_dir: Path, links_path: Path) -> dict[str, Any]:
    matrix = load_json(matrix_path)
    links = load_json(links_path)
    inventory = {str(feature["id"]): feature for feature in matrix.get("features", [])}

    scenarios: dict[str, dict[str, Any]] = {}
    for path in sorted(scenario_dir.glob("*.json")):
        data = load_json(path)
        scenario_id = str(data.get("id", path.stem))
        if scenario_id in scenarios:
            raise ValueError(f"duplicate scenario id: {scenario_id}")
        scenarios[scenario_id] = data

    unknown_scenarios = sorted(set(links) - set(scenarios))
    if unknown_scenarios:
        raise ValueError(
            "inventory links reference unknown scenarios: " + ", ".join(unknown_scenarios)
        )

    invalid_inventory: dict[str, list[str]] = {}
    linked: dict[str, list[str]] = {}
    for scenario_id, link in links.items():
        ids = [str(value) for value in link.get("inventoryIds", [])]
        bad = [value for value in ids if value not in inventory]
        if bad:
            invalid_inventory[scenario_id] = bad
        if ids:
            linked[scenario_id] = ids

    if invalid_inventory:
        details = "; ".join(
            f"{scenario}: {', '.join(ids)}" for scenario, ids in sorted(invalid_inventory.items())
        )
        raise ValueError(f"inventory links contain unknown inventory ids: {details}")

    unresolved = sorted(set(scenarios) - set(linked))
    linked_ids = sorted({item for ids in linked.values() for item in ids})
    linked_kinds: dict[str, int] = {}
    for inventory_id in linked_ids:
        kind = str(inventory[inventory_id].get("kind", "unknown"))
        linked_kinds[kind] = linked_kinds.get(kind, 0) + 1

    return {
        "schemaVersion": 1,
        "matrixSourceSha256": matrix.get("source", {}).get("sha256"),
        "scenarioCount": len(scenarios),
        "linkedScenarioCount": len(linked),
        "unresolvedScenarioCount": len(unresolved),
        "linkedInventoryCount": len(linked_ids),
        "linkedInventoryByKind": dict(sorted(linked_kinds.items())),
        "linkedScenarios": dict(sorted(linked.items())),
        "unresolvedScenarios": unresolved,
    }


def render_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Scenario inventory coverage",
        "",
        f"- Scenarios: **{report['scenarioCount']}**",
        f"- Linked to pinned AHK inventory: **{report['linkedScenarioCount']}**",
        f"- Unresolved source-inventory links: **{report['unresolvedScenarioCount']}**",
        f"- Distinct linked inventory entries: **{report['linkedInventoryCount']}**",
        "",
        "Unresolved does not mean the behavior is absent from the compiled compatibility target. It means the existing scenario cannot yet be tied unambiguously to an entry in the pinned AHK-source inventory; compiled/profile-only behavior stays explicit instead of receiving an invented source ID.",
        "",
        "## Linked scenarios",
        "",
        "| Scenario | Inventory IDs |",
        "| --- | --- |",
    ]
    for scenario, ids in report["linkedScenarios"].items():
        lines.append(f"| `{scenario}` | {', '.join(f'`{item}`' for item in ids)} |")
    lines.extend(["", "## Unresolved scenarios", ""])
    for scenario in report["unresolvedScenarios"]:
        lines.append(f"- `{scenario}`")
    lines.append("")
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--scenarios", type=Path, required=True)
    parser.add_argument("--links", type=Path, required=True)
    parser.add_argument("--json", dest="json_output", type=Path, required=True)
    parser.add_argument("--markdown", dest="markdown_output", type=Path, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    report = validate(args.matrix, args.scenarios, args.links)
    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    args.markdown_output.parent.mkdir(parents=True, exist_ok=True)
    args.json_output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown_output.write_text(render_markdown(report), encoding="utf-8")
    print(
        f"scenario inventory links: {report['linkedScenarioCount']}/{report['scenarioCount']} linked, "
        f"{report['unresolvedScenarioCount']} unresolved"
    )


if __name__ == "__main__":
    main()
