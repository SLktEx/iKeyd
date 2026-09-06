#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

ALLOWED_STATUSES = {"pass", "fail", "skipped", "pending"}
ALLOWED_AUTOMATED_STATUSES = {"pass", "fail", "skipped", "not-run"}


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def validate_plan(plan: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if plan.get("schemaVersion") != 1:
        errors.append("plan.schemaVersion must be 1")
    if not plan.get("planId"):
        errors.append("plan.planId is required")
    checks = plan.get("checks")
    if not isinstance(checks, list) or not checks:
        errors.append("plan.checks must be a non-empty list")
        return errors

    check_ids: set[str] = set()
    inventory_ids: list[str] = []
    for check in checks:
        check_id = check.get("id")
        if not check_id:
            errors.append("every plan check needs an id")
            continue
        if check_id in check_ids:
            errors.append(f"duplicate check id: {check_id}")
        check_ids.add(check_id)
        if not check.get("title"):
            errors.append(f"{check_id}: title is required")
        instructions = check.get("instructions")
        if not isinstance(instructions, list) or not instructions:
            errors.append(f"{check_id}: instructions must be non-empty")
        ids = check.get("inventoryIds")
        if not isinstance(ids, list) or not ids:
            errors.append(f"{check_id}: inventoryIds must be non-empty")
        else:
            inventory_ids.extend(ids)

    supplemental = plan.get("supplementalChecks", [])
    if not isinstance(supplemental, list):
        errors.append("plan.supplementalChecks must be a list")
    else:
        for check in supplemental:
            check_id = check.get("id")
            if not check_id:
                errors.append("every supplemental check needs an id")
                continue
            if check_id in check_ids:
                errors.append(f"duplicate check id: {check_id}")
            check_ids.add(check_id)
            if not check.get("instructions"):
                errors.append(f"{check_id}: instructions must be non-empty")

    duplicates = sorted({item for item in inventory_ids if inventory_ids.count(item) > 1})
    if duplicates:
        errors.append("duplicate inventory ids: " + ", ".join(duplicates))
    unique_count = len(set(inventory_ids))
    expected = plan.get("expectedRealWindowsInventoryCount")
    if unique_count != expected:
        errors.append(f"planned inventory count {unique_count} != expected {expected}")
    for inventory_id in inventory_ids:
        if not inventory_id.startswith("legacy-"):
            errors.append(f"invalid inventory id: {inventory_id}")

    return errors


def validate_report(plan: dict[str, Any], report: dict[str, Any], require_complete: bool) -> list[str]:
    errors: list[str] = []
    if report.get("schemaVersion") != 1:
        errors.append("report.schemaVersion must be 1")
    if report.get("planId") != plan.get("planId"):
        errors.append("report.planId does not match plan")
    if report.get("issue") != 59:
        errors.append("report.issue must be 59")

    expected_checks = {item["id"]: item for item in plan["checks"] + plan.get("supplementalChecks", [])}
    actual_items = report.get("checks")
    if not isinstance(actual_items, list):
        errors.append("report.checks must be a list")
        return errors
    actual_checks = {item.get("id"): item for item in actual_items if item.get("id")}
    if set(actual_checks) != set(expected_checks):
        missing = sorted(set(expected_checks) - set(actual_checks))
        extra = sorted(set(actual_checks) - set(expected_checks))
        if missing:
            errors.append("report is missing checks: " + ", ".join(missing))
        if extra:
            errors.append("report has unknown checks: " + ", ".join(extra))

    for check_id, check in actual_checks.items():
        status = check.get("status")
        if status not in ALLOWED_STATUSES:
            errors.append(f"{check_id}: invalid status {status!r}")
        expected_inventory = expected_checks[check_id].get("inventoryIds", [])
        if check.get("inventoryIds", []) != expected_inventory:
            errors.append(f"{check_id}: inventoryIds do not match the pinned plan")
        if status == "fail" and not str(check.get("notes", "")).strip():
            errors.append(f"{check_id}: failed checks require notes")

    automated = report.get("automated", {})
    for automated_id in ("legacyDifferential", "backendCompatibility", "clipboardCompatibility"):
        automated_item = automated.get(automated_id)
        if not isinstance(automated_item, dict):
            errors.append(f"automated.{automated_id} is required")
            continue
        status = automated_item.get("status")
        if status not in ALLOWED_AUTOMATED_STATUSES:
            errors.append(f"automated.{automated_id}: invalid status {status!r}")
        if status == "fail" and not str(automated_item.get("message", "")).strip():
            errors.append(f"automated.{automated_id}: failed checks require a message")

    binaries = report.get("binaries", {})
    legacy = binaries.get("legacy", {})
    if legacy.get("sha256") != plan.get("pinnedLegacyExeSha256"):
        errors.append("legacy executable SHA-256 does not match the pinned plan")

    summary = report.get("summary", {})
    planned_ids = {item for check in plan["checks"] for item in check["inventoryIds"]}
    if summary.get("plannedInventoryCount") != len(planned_ids):
        errors.append("summary.plannedInventoryCount does not match the plan")
    if summary.get("expectedRealWindowsInventoryCount") != plan.get("expectedRealWindowsInventoryCount"):
        errors.append("summary.expectedRealWindowsInventoryCount does not match the plan")

    if require_complete:
        incomplete = [cid for cid, item in actual_checks.items() if item.get("status") != "pass"]
        if incomplete:
            errors.append("checks are not complete: " + ", ".join(sorted(incomplete)))
        if automated.get("legacyDifferential", {}).get("status") != "pass":
            errors.append("automated legacy differential did not pass")
        if automated.get("backendCompatibility", {}).get("status") != "pass":
            errors.append("real-Win32 backend E2E did not pass")
        if automated.get("clipboardCompatibility", {}).get("status") not in {"pass", "skipped"}:
            errors.append("safe clipboard E2E was not attempted")
        if not report.get("environment", {}).get("japaneseImeConfigured"):
            errors.append("Japanese IME was not recorded as configured")
        if not binaries.get("ikeyd", {}).get("sha256"):
            errors.append("iKeyd binary SHA-256 is required for a complete report")
        if summary.get("complete") is not True:
            errors.append("summary.complete must be true")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate the #59 real-Windows verification plan/report.")
    parser.add_argument("--plan", type=Path, default=Path("tests/compatibility/real-windows-verification-plan.json"))
    parser.add_argument("--report", type=Path)
    parser.add_argument("--require-complete", action="store_true")
    args = parser.parse_args()

    plan = load_json(args.plan)
    errors = validate_plan(plan)
    if args.report:
        errors.extend(validate_report(plan, load_json(args.report), args.require_complete))
    elif args.require_complete:
        errors.append("--require-complete requires --report")

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    inventory_count = len({item for check in plan["checks"] for item in check["inventoryIds"]})
    print(f"real-Windows verification plan valid: {len(plan['checks'])} inventory groups, {inventory_count} inventory entries")
    if args.report:
        print("real-Windows verification report valid")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
