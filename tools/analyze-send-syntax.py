#!/usr/bin/env python3
"""Build the hotkeySKG AHK-v1 Send syntax inventory from a #54 matrix.

The compatibility scanner recognizes broad semantic surfaces. This second-stage
analyzer admits only real Send-family commands, deduplicates their expressions,
and, when the pinned source is supplied, expands the bounded dynamic variables
used by hotkeySKG (`key`, `string`, and the four `withFuncKey` arguments).
User-authored macro chunks (`temp` / `tempstr`) stay explicitly unbounded rather
than pretending that arbitrary AHK Send grammar is part of the compatibility
contract.
"""
from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from pathlib import Path
from typing import Any

SEND_COMMAND_RE = re.compile(r"^(SendInput|SendRaw|SendPlay|SendEvent|Send)\b\s*,?\s*(.*)$", re.I)
VARIABLE_RE = re.compile(r"%([^%]+)%")
BRACE_RE = re.compile(r"\{([^{}]+)\}")
MODIFIER_RE = re.compile(r"^([\^!+#]+)")
KEY_STATE_RE = re.compile(r"^(.+?)\s+(down|up)$", re.I)
REPEAT_RE = re.compile(r"^(.+?)\s+(\d+)$")
VK_SC_RE = re.compile(r"^vk[0-9a-f]+sc[0-9a-f]+$", re.I)
MEDIA_TOKENS = {"volume_up", "volume_down", "volume_mute", "media_next", "media_prev", "media_play_pause"}
ASSIGN_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\s*(?::=|=)\s*(.*)$")
FUNC_RE = re.compile(r"^func_([A-Za-z0-9]+)\(\)\{")
UNBOUNDED_DYNAMIC_VARIABLES = {"temp", "tempstr"}


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def strip_ahk_inline_comment(expression: str) -> str:
    for index, character in enumerate(expression):
        if character == ";" and (index == 0 or expression[index - 1].isspace()):
            return expression[:index].rstrip()
    return expression.strip()


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
        lower = normalized.lower()
        detail: dict[str, Any] = {"token": normalized, "family": "named-or-special-key"}
        if lower.startswith("click,"):
            detail.update(family="click")
            families.add("click-token")
        elif VK_SC_RE.fullmatch(normalized):
            detail.update(family="virtual-scan-code")
            families.add("virtual-scan-code-token")
        elif lower in MEDIA_TOKENS:
            detail.update(family="media")
            families.add("media-token")
        elif state := KEY_STATE_RE.fullmatch(normalized):
            detail.update(family="key-state", key=state.group(1), state=state.group(2).lower())
            families.add("key-state-token")
        elif repeat := REPEAT_RE.fullmatch(normalized):
            detail.update(family="repeat", key=repeat.group(1), count=int(repeat.group(2)))
            families.add("repeat-token")
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


def parse_ahk_string_arguments(arguments: str) -> list[str]:
    """Parse the quoted-string subset used by hotkeySKG's withFuncKey calls."""
    values: list[str] = []
    index = 0
    while index < len(arguments):
        while index < len(arguments) and arguments[index].isspace():
            index += 1
        if index >= len(arguments):
            break
        if arguments[index] == ',':
            values.append("")
            index += 1
            continue
        if arguments[index] != '"':
            next_comma = arguments.find(',', index)
            if next_comma < 0:
                next_comma = len(arguments)
            values.append(arguments[index:next_comma].strip())
            index = next_comma + 1
            continue

        index += 1
        value: list[str] = []
        while index < len(arguments):
            if arguments[index] == '"':
                if index + 1 < len(arguments) and arguments[index + 1] == '"':
                    value.append('"')
                    index += 2
                    continue
                index += 1
                break
            value.append(arguments[index])
            index += 1
        values.append("".join(value))
        while index < len(arguments) and arguments[index].isspace():
            index += 1
        if index < len(arguments) and arguments[index] == ',':
            index += 1
    return values


def _assignment_values(lines: list[str], prefix: str) -> list[str]:
    values: list[str] = []
    prefix_lower = prefix.lower()
    for raw in lines:
        match = ASSIGN_RE.match(raw.strip())
        if not match or not match.group(1).lower().startswith(prefix_lower):
            continue
        value = match.group(2).strip()
        if value and value not in values:
            values.append(value)
    return values


def source_dynamic_sets(source: Path) -> dict[str, dict[str, Any]]:
    lines = source.read_text(encoding="utf-8-sig").splitlines()
    default_keys = _assignment_values(lines, "defaultKey_")
    single_outputs = _assignment_values(lines, "singleStrokeS_") + _assignment_values(lines, "singleStrokeK_")
    chord_outputs = _assignment_values(lines, "resultOfKCmbS") + _assignment_values(lines, "resultOfKCmbK")
    sh_keys = _assignment_values(lines, "SHKey_")

    function_values: list[list[str]] = [[], [], [], []]
    function_sources: list[dict[str, Any]] = []
    current_function: str | None = None
    for line_number, raw in enumerate(lines, 1):
        text = raw.strip()
        if function := FUNC_RE.match(text):
            current_function = function.group(1)
        if not text.startswith("withFuncKey(") or "mkey=" in text:
            continue
        inner = text[len("withFuncKey("):-1]
        arguments = parse_ahk_string_arguments(inner)
        padded = (arguments + ["", "", "", ""])[:4]
        function_sources.append({"function": current_function, "line": line_number, "values": padded})
        for position, value in enumerate(padded):
            if value and value not in function_values[position]:
                function_values[position].append(value)

    string_values: list[str] = []
    for value in [*single_outputs, *chord_outputs, *sh_keys]:
        if value and value not in string_values:
            string_values.append(value)
    # outputChar is also called with Control/Alt prefixes around SHKey values.
    for prefix in ("^", "!"):
        for value in sh_keys:
            expanded = prefix + value
            if expanded not in string_values:
                string_values.append(expanded)

    return {
        "key": {"bounded": True, "values": default_keys, "source": "defaultKey_* assignments"},
        "string": {"bounded": True, "values": string_values, "source": "single/chord/SHKey outputChar callers"},
        "mkey": {"bounded": True, "values": function_values[0], "source": "withFuncKey argument 1"},
        "mhkey": {"bounded": True, "values": function_values[1], "source": "withFuncKey argument 2"},
        "hmkey": {"bounded": True, "values": function_values[2], "source": "withFuncKey argument 3"},
        "mskey": {"bounded": True, "values": function_values[3], "source": "withFuncKey argument 4"},
        "temp": {"bounded": False, "values": [], "source": "user-authored macro chunk"},
        "tempstr": {"bounded": False, "values": [], "source": "user-authored macro remainder"},
        "_withFuncKeyCalls": {"bounded": True, "values": function_sources, "source": "pinned source"},
    }


def expand_dynamic_expressions(expressions: list[dict[str, Any]], source: Path | None) -> dict[str, Any] | None:
    if source is None:
        return None

    sets = source_dynamic_sets(source)
    dynamic_items: list[dict[str, Any]] = []
    all_expanded: set[str] = set()
    family_counts: Counter[str] = Counter()

    for item in expressions:
        expression = item["expression"]
        variables = VARIABLE_RE.findall(expression)
        if not variables:
            continue
        normalized_variables = [value.strip().lower() for value in variables]
        if len(normalized_variables) != 1:
            dynamic_items.append({
                "expression": expression,
                "bounded": False,
                "variables": normalized_variables,
                "reason": "multiple dynamic variables are not statically expanded",
                "reachableExpressions": [],
            })
            continue

        variable = normalized_variables[0]
        definition = sets.get(variable)
        if definition is None or not definition["bounded"]:
            dynamic_items.append({
                "expression": expression,
                "bounded": False,
                "variables": [variable],
                "reason": (definition or {}).get("source", "unknown dynamic variable"),
                "reachableExpressions": [],
            })
            continue

        marker = f"%{variables[0]}%"
        expanded_values: list[dict[str, Any]] = []
        for value in definition["values"]:
            expanded = expression.replace(marker, str(value))
            analyzed = classify_expression(expanded)
            expanded_values.append({
                "expression": expanded,
                "families": analyzed["families"],
                "braceTokens": analyzed["braceTokens"],
            })
            all_expanded.add(expanded)
            family_counts.update(analyzed["families"])

        dynamic_items.append({
            "expression": expression,
            "bounded": True,
            "variables": [variable],
            "source": definition["source"],
            "reachableExpressionCount": len(expanded_values),
            "reachableExpressions": expanded_values,
        })

    bounded = sum(bool(item["bounded"]) for item in dynamic_items)
    return {
        "summary": {
            "dynamicExpressionCount": len(dynamic_items),
            "boundedDynamicExpressionCount": bounded,
            "unboundedDynamicExpressionCount": len(dynamic_items) - bounded,
            "uniqueReachableExpandedExpressionCount": len(all_expanded),
            "reachableFamilyCounts": dict(sorted(family_counts.items())),
        },
        "variables": {
            key: {
                "bounded": value["bounded"],
                "source": value["source"],
                "valueCount": len(value["values"]),
                "values": value["values"],
            }
            for key, value in sets.items()
            if not key.startswith("_")
        },
        "withFuncKeyCalls": sets["_withFuncKeyCalls"]["values"],
        "expressions": dynamic_items,
    }


def build_inventory(matrix: dict[str, Any], source: Path | None = None) -> dict[str, Any]:
    send_features = [feature for feature in matrix.get("features", []) if feature.get("kind") == "send"]
    valid: list[dict[str, Any]] = []
    rejected: list[dict[str, Any]] = []
    normalized_comment_count = 0

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
        raw_expression = str(feature.get("details", {}).get("expression", match.group(2))).strip()
        expression = strip_ahk_inline_comment(raw_expression)
        if expression != raw_expression:
            normalized_comment_count += 1
        analyzed = classify_expression(expression)
        valid.append({
            "id": feature.get("id"),
            "line": feature.get("line"),
            "owner": feature.get("owner"),
            "windowContext": feature.get("windowContext"),
            "command": command,
            "expression": expression,
            "rawExpression": raw_expression,
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
            "rawExpressions": [],
        })
        group["inventoryIds"].append(item["id"])
        if item["owner"] not in group["owners"]:
            group["owners"].append(item["owner"])
        group["lines"].append(item["line"])
        if item["rawExpression"] not in group["rawExpressions"]:
            group["rawExpressions"].append(item["rawExpression"])

    expressions = sorted(grouped.values(), key=lambda item: (item["command"].lower(), item["expression"]))
    family_counts = Counter(family for item in expressions for family in item["families"])
    command_counts = Counter(item["command"].lower() for item in valid)
    brace_tokens = sorted({token["token"] for item in expressions for token in item["braceTokens"]})
    reachability = expand_dynamic_expressions(expressions, source)

    return {
        "schemaVersion": 2,
        "sourceSha256": matrix.get("source", {}).get("sha256"),
        "summary": {
            "scannerSendFeatureCount": len(send_features),
            "actualSendFeatureCount": len(valid),
            "scannerFalsePositiveCount": len(rejected),
            "uniqueExpressionCount": len(expressions),
            "dynamicExpressionCount": sum(item["dynamic"] for item in expressions),
            "staticExpressionCount": sum(not item["dynamic"] for item in expressions),
            "inlineCommentNormalizedCount": normalized_comment_count,
            "commandCounts": dict(sorted(command_counts.items())),
            "familyCounts": dict(sorted(family_counts.items())),
            "braceTokenCount": len(brace_tokens),
        },
        "braceTokens": brace_tokens,
        "expressions": expressions,
        "dynamicReachability": reachability,
        "scannerFalsePositives": rejected,
    }


def render_markdown(report: dict[str, Any]) -> str:
    summary = report["summary"]
    lines = [
        "# hotkeySKG Send syntax inventory", "",
        f"Pinned source SHA-256: `{report.get('sourceSha256')}`", "",
        "## Summary", "",
        f"- Scanner `send` features: **{summary['scannerSendFeatureCount']}**",
        f"- Actual Send-family commands: **{summary['actualSendFeatureCount']}**",
        f"- Scanner false positives excluded: **{summary['scannerFalsePositiveCount']}**",
        f"- Unique Send expressions: **{summary['uniqueExpressionCount']}**",
        f"- Static expressions: **{summary['staticExpressionCount']}**",
        f"- Dynamic expressions: **{summary['dynamicExpressionCount']}**",
        f"- Inline source comments normalized: **{summary['inlineCommentNormalizedCount']}**", "",
        "## Syntax families", "", "| Family | Expressions |", "| --- | ---: |",
    ]
    for family, count in summary["familyCounts"].items():
        lines.append(f"| `{family}` | {count} |")

    reachability = report.get("dynamicReachability")
    if reachability:
        dynamic_summary = reachability["summary"]
        lines.extend([
            "", "## Dynamic reachability", "",
            f"- Bounded source expressions: **{dynamic_summary['boundedDynamicExpressionCount']}**",
            f"- Unbounded user-macro expressions: **{dynamic_summary['unboundedDynamicExpressionCount']}**",
            f"- Unique bounded expansions: **{dynamic_summary['uniqueReachableExpandedExpressionCount']}**", "",
            "`temp` / `tempstr` are user-authored macro chunks. They are intentionally not treated as a finite AHK grammar requirement; unsupported runtime Send forms must produce diagnostics.", "",
        ])

    lines.extend(["", "## Expressions", "", "| Command | Expression | Families | Inventory IDs |", "| --- | --- | --- | --- |"])
    for item in report["expressions"]:
        expression = item["expression"].replace("|", "\\|")
        families = ", ".join(f"`{value}`" for value in item["families"])
        ids = ", ".join(f"`{value}`" for value in item["inventoryIds"])
        lines.append(f"| `{item['command']}` | `{expression}` | {families} | {ids} |")

    lines.extend(["", "## Brace tokens", ""])
    for token in report["braceTokens"]:
        lines.append(f"- `{token}`")
    if not report["braceTokens"]:
        lines.append("- none")

    lines.extend(["", "## Scanner false positives", ""])
    for item in report["scannerFalsePositives"]:
        lines.append(f"- `{item['id']}` line {item['line']}: `{item['text']}`")
    if not report["scannerFalsePositives"]:
        lines.append("- none")
    lines.append("")
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--source", type=Path)
    parser.add_argument("--json", dest="json_output", type=Path, required=True)
    parser.add_argument("--markdown", dest="markdown_output", type=Path, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    report = build_inventory(load_json(args.matrix), args.source)
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
    if report["dynamicReachability"]:
        reach = report["dynamicReachability"]["summary"]
        print(
            "dynamic reachability: "
            f"{reach['boundedDynamicExpressionCount']} bounded, "
            f"{reach['unboundedDynamicExpressionCount']} unbounded, "
            f"{reach['uniqueReachableExpandedExpressionCount']} unique expansions"
        )


if __name__ == "__main__":
    main()
