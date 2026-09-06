#!/usr/bin/env python3
"""Build the hotkeySKG AHK-v1 Send syntax inventory from a #54 matrix.

The compatibility scanner intentionally recognizes broad semantic surfaces. This
second-stage analyzer is stricter: only actual Send-family commands are admitted
to the syntax inventory, so identifiers such as `SendMessage` are reported as
scanner false positives rather than becoming compatibility requirements.
"""
from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from pathlib import Path
from typing import Any

SEND_COMMAND_RE = re.compile(r"^(SendInput|SendRaw|SendPlay|SendEvent|Send)\b\s*,?\s*(.*)$", re.I)
VARIABLE_RE = re.compile(r"%[^%]+%")
BRACE_RE = re.compile(r"\{([^{}]+)\}")
MODIFIER_RE = re.compile(r"^([\^!+#]+)")
KEY_STATE_RE = re.compile(r"^(.+?)\s+(down|up)$", re.I)
REPEAT_RE = re.compile(r"^(.+?)\s+(\d+)$")


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def classify_expression(expression: str) -> dict[str, Any]:
    expression = expression.strip()
    dynamic = bool(VARIABLE_RE.search(expression))
    modifier = MODIFIER_RE.match(expression)
    tokens = BRACE_RE.findall(expression)
    token_details: list[dict[str, Any]] = []
    families: set[str] = set()

    if dynamic:
        families.add("dynamic-expression")
    if modifier:
        families.add("modifier-prefix")

    if tokens:
        families.add("brace-token")
        outside = BRACE_RE.sub("", expression)
        if outside.strip("^!+#"):
            families.add("mixed-text-and-token")

    for token in tokens:
        normalized = token.strip()
        detail: dict[str, Any] = {"token": normalized, "family": "named-or-special-key"}
        if state := KEY_STATE_RE.fullmatch(normalized):
            detail.update(family="key-state", key=state.group(1), state=state.group(2).lower())
            families.add("key-state-token")
        elif repeat := REPEAT_RE.fullmatch(normalized):
            detail.update(family="repeat", key=repeat.group(1), count=int(repeat.group(2)))
            families.add("repeat-token")
        elif normalized.lower().startswith("click"):
            detail.update(family="click")
            families.add("click-token")
        token_details.append(detail)

    if not tokens and not modifier and not dynamic:
        families.add("plain-text")

    return {
        "expression": expression,
        "dynamic": dynamic,
        "modifierPrefix": modifier.group(1) if modifier else None,
        "braceTokens": token_details,
        "families": sorted(families),
    }


def build_inventory(matrix: dict[str, Any]) -> dict[str, Any]:
    send_features = [feature for feature in matrix.get("features", []) if feature.get("kind") == "send"]
    valid: list[dict[str, Any]] = []
    rejected: list[dict[str, Any]] = []

    for feature in send_features:
        text = str(feature.get("text", "")).strip()
        match = SEND_COMMAND_RE.match(text)
        if not match:
            rejected.append({
                "id": feature.get("id"),
                "line": feature.get("line"),
                "owner": feature.get("owner"),
                "text": text,
                "reason": "send-tagged feature is not an actual Send-family command",
            })
            continue

        command = match.group(1)
        expression = str(feature.get("details", {}).get("expression", match.group(2))).strip()
        analyzed = classify_expression(expression)
        valid.append({
            "id": feature.get("id"),
            "line": feature.get("line"),
            "owner": feature.get("owner"),
            "windowContext": feature.get("windowContext"),
            "command": command,
            "expression": expression,
            "families": analyzed["families"],
            "dynamic": analyzed["dynamic"],
            "modifierPrefix": analyzed["modifierPrefix"],
            "braceTokens": analyzed["braceTokens"],
        })

    grouped: dict[tuple[str, str], dict[str, Any]] = {}
    for item in valid:
        key = (item["command"].lower(), item["expression"])
        group = grouped.setdefault(key, {
            "command": item["command"],
            "expression": item["expression"],
            "families": item["families"],
            "dynamic": item["dynamic"],
            "modifierPrefix": item["modifierPrefix"],
            "braceTokens": item["braceTokens"],
            "inventoryIds": [],
            "owners": [],
            "lines": [],
        })
        group["inventoryIds"].append(item["id"])
        if item["owner"] not in group["owners"]:
            group["owners"].append(item["owner"])
        group["lines"].append(item["line"])

    expressions = sorted(grouped.values(), key=lambda item: (item["command"].lower(), item["expression"]))
    family_counts = Counter(family for item in expressions for family in item["families"])
    command_counts = Counter(item["command"].lower() for item in valid)
    brace_tokens = sorted({token["token"] for item in expressions for token in item["braceTokens"]})

    return {
        "schemaVersion": 1,
        "sourceSha256": matrix.get("source", {}).get("sha256"),
        "summary": {
            "scannerSendFeatureCount": len(send_features),
            "actualSendFeatureCount": len(valid),
            "scannerFalsePositiveCount": len(rejected),
            "uniqueExpressionCount": len(expressions),
            "dynamicExpressionCount": sum(item["dynamic"] for item in expressions),
            "staticExpressionCount": sum(not item["dynamic"] for item in expressions),
            "commandCounts": dict(sorted(command_counts.items())),
            "familyCounts": dict(sorted(family_counts.items())),
            "braceTokenCount": len(brace_tokens),
        },
        "braceTokens": brace_tokens,
        "expressions": expressions,
        "scannerFalsePositives": rejected,
    }


def render_markdown(report: dict[str, Any]) -> str:
    summary = report["summary"]
    lines = [
        "# hotkeySKG Send syntax inventory",
        "",
        f"Pinned source SHA-256: `{report.get('sourceSha256')}`",
        "",
        "## Summary",
        "",
        f"- Scanner `send` features: **{summary['scannerSendFeatureCount']}**",
        f"- Actual Send-family commands: **{summary['actualSendFeatureCount']}**",
        f"- Scanner false positives excluded: **{summary['scannerFalsePositiveCount']}**",
        f"- Unique Send expressions: **{summary['uniqueExpressionCount']}**",
        f"- Static expressions: **{summary['staticExpressionCount']}**",
        f"- Dynamic expressions: **{summary['dynamicExpressionCount']}**",
        "",
        "## Syntax families",
        "",
        "| Family | Expressions |",
        "| --- | ---: |",
    ]
    for family, count in summary["familyCounts"].items():
        lines.append(f"| `{family}` | {count} |")

    lines.extend(["", "## Expressions", "", "| Command | Expression | Families | Inventory IDs |", "| --- | --- | --- | --- |"])
    for item in report["expressions"]:
        expression = item["expression"].replace("|", "\\|")
        families = ", ".join(f"`{value}`" for value in item["families"])
        ids = ", ".join(f"`{value}`" for value in item["inventoryIds"])
        lines.append(f"| `{item['command']}` | `{expression}` | {families} | {ids} |")

    lines.extend(["", "## Brace tokens", ""])
    if report["braceTokens"]:
        for token in report["braceTokens"]:
            lines.append(f"- `{token}`")
    else:
        lines.append("- none")

    lines.extend(["", "## Scanner false positives", ""])
    if report["scannerFalsePositives"]:
        for item in report["scannerFalsePositives"]:
            lines.append(f"- `{item['id']}` line {item['line']}: `{item['text']}`")
    else:
        lines.append("- none")
    lines.append("")
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--json", dest="json_output", type=Path, required=True)
    parser.add_argument("--markdown", dest="markdown_output", type=Path, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    report = build_inventory(load_json(args.matrix))
    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    args.markdown_output.parent.mkdir(parents=True, exist_ok=True)
    args.json_output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown_output.write_text(render_markdown(report), encoding="utf-8")
    summary = report["summary"]
    print(
        "send syntax inventory: "
        f"{summary['actualSendFeatureCount']} commands, "
        f"{summary['uniqueExpressionCount']} unique expressions, "
        f"{summary['scannerFalsePositiveCount']} scanner false positives excluded"
    )


if __name__ == "__main__":
    main()
