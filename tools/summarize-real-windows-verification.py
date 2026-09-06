#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

INCOMPLETE_STATUSES = {"pending", "fail", "skipped"}
AUTOMATED_NAMES = {
    "legacyDifferential": "Real-IME legacy differential",
    "backendCompatibility": "Real-Win32 backend E2E",
    "clipboardCompatibility": "Safe clipboard E2E",
    "physicalInputCompatibility": "Windows hook/SendInput E2E",
}


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def build_summary(plan: dict[str, Any], report: dict[str, Any] | None) -> str:
    report_checks: dict[str, dict[str, Any]] = {}
    automated: dict[str, Any] = {}
    if report is not None:
        report_checks = {
            item.get("id"): item
            for item in report.get("checks", [])
            if isinstance(item, dict) and item.get("id")
        }
        automated = report.get("automated", {}) if isinstance(report.get("automated"), dict) else {}

    lines: list[str] = ["# #59 remaining real-Windows verification", ""]

    lines.append("## Automated evidence")
    for check_id, title in AUTOMATED_NAMES.items():
        item = automated.get(check_id, {}) if report is not None else {}
        status = item.get("status", "not-run") if isinstance(item, dict) else "not-run"
        message = str(item.get("message", "")).strip() if isinstance(item, dict) else ""
        suffix = f" — {message}" if message else ""
        lines.append(f"- [{_checkbox(status == 'pass' or (check_id == 'clipboardCompatibility' and status == 'skipped'))}] {title}: `{status}`{suffix}")
    lines.append("")

    all_checks = list(plan.get("checks", [])) + list(plan.get("supplementalChecks", []))
    incomplete: list[tuple[dict[str, Any], str, str]] = []
    passed = 0
    for planned in all_checks:
        check_id = planned["id"]
        observed = report_checks.get(check_id)
        status = observed.get("status", "pending") if observed else "pending"
        notes = str(observed.get("notes", "")).strip() if observed else ""
        if status == "pass":
            passed += 1
        else:
            incomplete.append((planned, status, notes))

    lines.append("## Manual / interactive checks still open")
    if not incomplete:
        lines.append("- None. All plan and supplemental checks are marked `pass`.")
    else:
        for planned, status, notes in incomplete:
            check_id = planned["id"]
            title = planned.get("title", check_id)
            inventory_ids = planned.get("inventoryIds", [])
            required = planned.get("requiredEnvironment", [])
            lines.extend(["", f"### {title} (`{check_id}`)", "", f"Status: **{status}**"])
            if inventory_ids:
                lines.append(f"Inventory: **{len(inventory_ids)}** entries")
            if required:
                lines.append("Required environment: " + ", ".join(f"`{item}`" for item in required))
            if notes:
                lines.append(f"Existing notes: {notes}")
            lines.append("")
            lines.append("Steps:")
            for instruction in planned.get("instructions", []):
                lines.append(f"- [ ] {instruction}")

    lines.extend([
        "",
        "## Progress",
        f"- Manual checks passed: **{passed}/{len(all_checks)}**",
        f"- Manual checks remaining: **{len(incomplete)}**",
    ])

    if report is not None:
        environment = report.get("environment", {}) if isinstance(report.get("environment"), dict) else {}
        summary = report.get("summary", {}) if isinstance(report.get("summary"), dict) else {}
        lines.extend([
            f"- Japanese IME detected: **{bool(environment.get('japaneseImeConfigured'))}**",
            f"- Report complete: **{bool(summary.get('complete'))}**",
        ])

    return "\n".join(lines) + "\n"


def _checkbox(checked: bool) -> str:
    return "x" if checked else " "


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Print only the remaining #59 real-Windows verification work."
    )
    parser.add_argument(
        "--plan",
        type=Path,
        default=Path("tests/compatibility/real-windows-verification-plan.json"),
    )
    parser.add_argument("--report", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    plan = load_json(args.plan)
    report = load_json(args.report) if args.report else None
    text = build_summary(plan, report)

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text, encoding="utf-8")
        print(args.output)
    else:
        print(text, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
