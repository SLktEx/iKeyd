#!/usr/bin/env python3
"""Inventory hotkeySKG behavior and render a compatibility matrix.

This is a deliberately targeted AutoHotkey v1 scanner rather than a general AHK
parser. Unknown coverage stays unknown so the output remains a useful work queue
for #46 instead of silently assuming compatibility.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable

STATUS_FIELDS = (
    "implementation",
    "unit",
    "scenario",
    "exeDiff",
    "ahkDiff",
    "realWindows",
    "intentionalDifference",
)
DEFAULT_STATUS = {name: "unknown" for name in STATUS_FIELDS}

ASSIGN = r"\s*(?::=|=)\s*"
SINGLE_RE = re.compile(rf"singleStroke([SK])_([A-Za-z0-9]+){ASSIGN}(.*)$", re.I)
CHORD_RE = re.compile(
    rf"kCmb([SK])(\d+){ASSIGN}flag_([A-Za-z0-9]+)\s*\|\s*flag_([A-Za-z0-9]+)",
    re.I,
)
CHORD_RESULT_RE = re.compile(rf"resultOfKCmb([SK])(\d+){ASSIGN}(.*)$", re.I)
FUNCTION_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*\{\s*$")
LABEL_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*):\s*$")
HOTKEY_RE = re.compile(r"^(?!#)(.+?)::(.*)$")
SEND_RE = re.compile(r"\b(Send|SendInput|SendRaw|SendPlay|SendEvent)\s*,?\s*(.*)$", re.I)
COMMAND_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\s*,?\s*(.*)$")
BRANCH_RE = re.compile(r"^(?:}\s*)?(?:else\s+)?if\b|^#If\b", re.I)
CONTROL_FLOW_NAMES = {"if", "while", "for", "loop", "switch", "catch", "try", "else"}

WINDOW_COMMANDS = {
    "winactivate", "winget", "wingetpos", "wingettitle", "winhide", "winmaximize",
    "winminimize", "winmove", "winrestore", "winset", "winshow", "postmessage",
}
MOUSE_COMMANDS = {"click", "mousegetpos", "mousemove"}
CLIPBOARD_COMMANDS = {"clipwait"}
MEDIA_COMMANDS = {"soundget", "soundset"}
PROCESS_COMMANDS = {"process", "run", "runwait"}
LIFECYCLE_COMMANDS = {"exitapp", "reload", "suspend"}
UI_COMMANDS = {"gui", "menu", "msgbox", "inputbox"}
INPUT_STATE_COMMANDS = {"getkeystate", "keywait", "settimer", "input"}
MEDIA_TOKENS = ("VOLUME_", "MEDIA_", "VOLUME_MUTE")
MODE_TOKENS = ("gmode", "gimode", "SMODE", "RMODE", "TMODE", "KMODE")
LAYER_TOKENS = ("fstate", "flag", '"M"', '"H"', '"S"', '"K"', '"A"')
IME_TOKENS = ("IME_", "ImmGetDefaultIMEWnd", "imeget", "conversion", "gimode")
MACRO_TOKENS = ("macro", "{wait", "{calc", "{hk", "increment", "InputBox")
CLIPBOARD_TOKENS = ("clipboard", "OnClipboardChange", "ClipWait", "clip00", "gpaste")
PROCESS_TOKENS = ("#IfWin", "ahk_class", "ahk_exe", "WinActive(", "IfWin")


@dataclass
class Feature:
    kind: str
    line: int
    text: str
    owner: str
    window_context: str | None = None
    tags: list[str] = field(default_factory=list)
    details: dict[str, Any] = field(default_factory=dict)
    feature_id: str = ""
    coverage: dict[str, str] = field(default_factory=lambda: dict(DEFAULT_STATUS))
    evidence: list[str] = field(default_factory=list)
    classification: str = "unknown"

    def as_dict(self) -> dict[str, Any]:
        return {
            "id": self.feature_id,
            "kind": self.kind,
            "line": self.line,
            "owner": self.owner,
            "windowContext": self.window_context,
            "text": self.text,
            "tags": self.tags,
            "details": self.details,
            "coverage": self.coverage,
            "evidence": self.evidence,
            "classification": self.classification,
        }


def normalized(text: str) -> str:
    return re.sub(r"\s+", " ", text.strip())


def contains_any(text: str, tokens: Iterable[str]) -> bool:
    lower = text.lower()
    return any(token.lower() in lower for token in tokens)


def add_unique(items: list[str], *values: str) -> None:
    for value in values:
        if value and value not in items:
            items.append(value)


def semantic_tags(text: str, command: str | None, window_context: str | None) -> list[str]:
    tags: list[str] = []
    cmd = (command or "").lower()
    if SEND_RE.search(text):
        add_unique(tags, "send")
    if cmd in WINDOW_COMMANDS or contains_any(text, ("Win", "PostMessage")):
        add_unique(tags, "window")
    if cmd in MOUSE_COMMANDS or contains_any(text, ("MouseMove", "MouseGetPos", "click,")):
        add_unique(tags, "mouse")
    if cmd in MEDIA_COMMANDS or contains_any(text, MEDIA_TOKENS):
        add_unique(tags, "media")
    if cmd in PROCESS_COMMANDS:
        add_unique(tags, "process")
    if cmd in LIFECYCLE_COMMANDS:
        add_unique(tags, "lifecycle")
    if cmd in UI_COMMANDS:
        add_unique(tags, "ui")
    if cmd in CLIPBOARD_COMMANDS or contains_any(text, CLIPBOARD_TOKENS):
        add_unique(tags, "clipboard")
    if contains_any(text, MACRO_TOKENS):
        add_unique(tags, "macro")
    if contains_any(text, IME_TOKENS):
        add_unique(tags, "ime")
    if contains_any(text, MODE_TOKENS):
        add_unique(tags, "mode")
    if contains_any(text, LAYER_TOKENS):
        add_unique(tags, "layer")
    if cmd in INPUT_STATE_COMMANDS or re.search(r"\bup\b", text, re.I):
        add_unique(tags, "input-state")
    if window_context or contains_any(text, PROCESS_TOKENS):
        add_unique(tags, "process-specific")
    if "A_OSVersion" in text:
        add_unique(tags, "os-specific")
    return tags


def classify_kind(text: str, command: str | None, tags: list[str]) -> str | None:
    if SEND_RE.search(text):
        return "send"
    cmd = (command or "").lower()
    if cmd in WINDOW_COMMANDS:
        return "window-operation"
    if cmd in MOUSE_COMMANDS:
        return "mouse-operation"
    if cmd in MEDIA_COMMANDS or "media" in tags:
        return "media-operation"
    if cmd in PROCESS_COMMANDS:
        return "process-operation"
    if cmd in LIFECYCLE_COMMANDS:
        return "lifecycle-operation"
    if cmd in UI_COMMANDS:
        return "ui-operation"
    if cmd in CLIPBOARD_COMMANDS or "clipboard" in tags:
        return "clipboard-operation"
    if "macro" in tags:
        return "macro-operation"
    if "ime" in tags and ("DllCall" in text or "IME_" in text or "imeget" in text.lower()):
        return "ime-operation"
    if cmd in INPUT_STATE_COMMANDS:
        return "input-state-operation"
    if BRANCH_RE.search(text) and any(
        tag in tags for tag in ("mode", "layer", "ime", "process-specific", "os-specific", "input-state")
    ):
        return "branch"
    return None


def stable_ids(features: list[Feature]) -> None:
    occurrences: Counter[str] = Counter()
    for feature in features:
        signature = "|".join(
            [feature.kind, feature.owner, feature.window_context or "", normalized(feature.text)]
        )
        occurrences[signature] += 1
        digest = hashlib.sha1(signature.encode("utf-8")).hexdigest()[:10]
        suffix = f"-{occurrences[signature]}" if occurrences[signature] > 1 else ""
        feature.feature_id = f"legacy-{feature.kind}-{digest}{suffix}"


def function_match(text: str) -> re.Match[str] | None:
    match = FUNCTION_RE.fullmatch(text)
    if match and match.group(1).lower() not in CONTROL_FLOW_NAMES:
        return match
    return None


def scan_source(source: Path) -> list[Feature]:
    lines = source.read_text(encoding="utf-8-sig").splitlines()
    features: list[Feature] = []
    owner = "top-level"
    window_context: str | None = None

    for index, raw in enumerate(lines):
        line_no = index + 1
        text = raw.strip()
        if not text or text.startswith(";"):
            continue

        if text.lower().startswith("#ifwin") or re.match(r"^#If\b", text, re.I):
            window_context = text if text.lower() not in {"#ifwinactive", "#ifwinexist", "#if"} else None
            features.append(Feature(
                "context", line_no, text, owner, window_context,
                semantic_tags(text, None, window_context),
            ))
            continue

        if match := function_match(text):
            owner = f"function:{match.group(1)}"
            features.append(Feature(
                "function", line_no, text, owner, window_context,
                semantic_tags(text, None, window_context),
                {"name": match.group(1), "parameters": match.group(2).strip()},
            ))
            continue

        if match := LABEL_RE.fullmatch(text):
            owner = f"label:{match.group(1)}"
            features.append(Feature(
                "label", line_no, text, owner, window_context,
                semantic_tags(text, None, window_context), {"name": match.group(1)},
            ))
            continue

        if match := SINGLE_RE.fullmatch(text):
            mode, key, output = match.groups()
            features.append(Feature(
                "single-stroke", line_no, text, owner, window_context,
                ["keymap", f"mode-{mode.upper()}"],
                {"mode": mode.upper(), "key": key, "output": output},
            ))
            continue

        if match := CHORD_RE.fullmatch(text):
            mode, ordinal, first, second = match.groups()
            output = None
            if index + 1 < len(lines):
                result = CHORD_RESULT_RE.fullmatch(lines[index + 1].strip())
                if result and result.group(1).upper() == mode.upper() and result.group(2) == ordinal:
                    output = result.group(3)
            features.append(Feature(
                "chord", line_no, text, owner, window_context,
                ["keymap", "chord", f"mode-{mode.upper()}"],
                {
                    "mode": mode.upper(),
                    "ordinal": int(ordinal),
                    "keys": [first, second],
                    "output": output,
                },
            ))
            continue

        if match := HOTKEY_RE.fullmatch(text):
            trigger, body = match.groups()
            tags = semantic_tags(body, None, window_context)
            add_unique(tags, "hotkey")
            if trigger.lower().endswith(" up"):
                add_unique(tags, "input-state")
            owner = f"hotkey:{trigger}"
            features.append(Feature(
                "hotkey", line_no, text, owner, window_context, tags,
                {"trigger": trigger, "body": body.strip()},
            ))
            body_text = body.strip()
            if body_text:
                command_match = COMMAND_RE.match(body_text)
                command = command_match.group(1) if command_match else None
                body_tags = semantic_tags(body_text, command, window_context)
                kind = classify_kind(body_text, command, body_tags)
                if kind:
                    details: dict[str, Any] = {"inlineHotkey": trigger}
                    if send := SEND_RE.search(body_text):
                        details.update(sendCommand=send.group(1), expression=send.group(2).strip())
                    if command:
                        details.setdefault("command", command)
                    features.append(Feature(
                        kind, line_no, body_text, owner, window_context, body_tags, details,
                    ))
            continue

        command_match = COMMAND_RE.match(text)
        command = command_match.group(1) if command_match else None
        tags = semantic_tags(text, command, window_context)
        kind = classify_kind(text, command, tags)
        if kind:
            details: dict[str, Any] = {}
            if send := SEND_RE.search(text):
                details.update(sendCommand=send.group(1), expression=send.group(2).strip())
            if command:
                details.setdefault("command", command)
            features.append(Feature(kind, line_no, text, owner, window_context, tags, details))

    stable_ids(features)
    return features


def load_json(path: Path | None, fallback: Any) -> Any:
    if path is None or not path.exists():
        return fallback
    return json.loads(path.read_text(encoding="utf-8"))


def matches_rule(feature: Feature, match: dict[str, Any]) -> bool:
    if "kind" in match:
        kinds = match["kind"] if isinstance(match["kind"], list) else [match["kind"]]
        if feature.kind not in kinds:
            return False
    if "tag" in match:
        tags = match["tag"] if isinstance(match["tag"], list) else [match["tag"]]
        if not any(tag in feature.tags for tag in tags):
            return False
    if "id" in match and feature.feature_id != match["id"]:
        return False
    if "textRegex" in match and not re.search(match["textRegex"], feature.text, re.I):
        return False
    if "ownerRegex" in match and not re.search(match["ownerRegex"], feature.owner, re.I):
        return False
    if "windowContextRegex" in match and not re.search(
        match["windowContextRegex"], feature.window_context or "", re.I
    ):
        return False
    return True


def derive_classification(coverage: dict[str, str]) -> str:
    implementation = coverage.get("implementation", "unknown")
    scenario = coverage.get("scenario", "unknown")
    unit = coverage.get("unit", "unknown")
    real = coverage.get("realWindows", "unknown")
    intentional = coverage.get("intentionalDifference", "unknown")

    if implementation == "missing":
        return "implementation-missing"
    if implementation == "unknown":
        return "unknown"
    if scenario == "missing":
        return "scenario-missing"
    if unit in {"missing", "unknown"} and scenario in {"missing", "unknown"}:
        return "implemented-but-untested"
    if real == "required":
        return "real-windows-verification-required"
    if intentional == "yes":
        return "intentional-difference"
    if any(value in {"unknown", "partial", "unverified"} for value in coverage.values()):
        return "partially-verified"
    return "implemented-and-verified"


def apply_coverage(features: list[Feature], config: dict[str, Any]) -> None:
    defaults = dict(DEFAULT_STATUS)
    defaults.update(config.get("defaults", {}))
    for feature in features:
        feature.coverage = dict(defaults)
        for rule in config.get("rules", []):
            if matches_rule(feature, rule.get("match", {})):
                feature.coverage.update(rule.get("set", {}))
                for evidence in rule.get("evidence", []):
                    if evidence not in feature.evidence:
                        feature.evidence.append(evidence)
        feature.classification = derive_classification(feature.coverage)


def scenario_index(directory: Path | None) -> dict[str, Any]:
    if directory is None or not directory.exists():
        return {"count": 0, "files": [], "inventoryIds": []}
    files = sorted(directory.glob("*.json"))
    inventory_ids: set[str] = set()
    entries: list[dict[str, Any]] = []
    for path in files:
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            entries.append({"path": str(path), "parse": "failed"})
            continue
        ids = data.get("inventoryIds", [])
        if isinstance(ids, list):
            inventory_ids.update(str(item) for item in ids)
        entries.append({
            "path": str(path),
            "id": data.get("id", path.stem),
            "tags": data.get("tags", []),
            "inventoryIds": ids,
        })
    return {"count": len(files), "files": entries, "inventoryIds": sorted(inventory_ids)}


def profile_summary(path: Path | None) -> dict[str, Any]:
    data = load_json(path, {})
    if not data:
        return {"available": False}
    single = data.get("singleStroke", {})
    chords = data.get("chords", {})
    return {
        "available": True,
        "singleStroke": {mode: len(values) for mode, values in single.items() if isinstance(values, dict)},
        "chords": {mode: len(values) for mode, values in chords.items() if isinstance(values, list)},
    }


def build_report(
    source: Path,
    features: list[Feature],
    scenarios: dict[str, Any],
    profile: dict[str, Any],
) -> dict[str, Any]:
    by_kind = Counter(feature.kind for feature in features)
    by_classification = Counter(feature.classification for feature in features)
    by_tag = Counter(tag for feature in features for tag in feature.tags)
    return {
        "schemaVersion": 1,
        "source": {
            "name": source.name,
            "sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
            "lineCount": len(source.read_text(encoding="utf-8-sig").splitlines()),
        },
        "summary": {
            "featureCount": len(features),
            "byKind": dict(sorted(by_kind.items())),
            "byClassification": dict(sorted(by_classification.items())),
            "byTag": dict(sorted(by_tag.items())),
            "unknownCount": sum(f.classification == "unknown" for f in features),
            "missingCount": sum(
                f.classification in {"implementation-missing", "scenario-missing"} for f in features
            ),
            "scenarioCount": scenarios["count"],
        },
        "profile": profile,
        "scenarioIndex": scenarios,
        "features": [feature.as_dict() for feature in features],
    }


def markdown(report: dict[str, Any]) -> str:
    summary = report["summary"]
    lines = [
        "# hotkeySKG compatibility matrix",
        "",
        f"Source SHA-256: `{report['source']['sha256']}`",
        "",
        "## Summary",
        "",
        f"- Inventory features: **{summary['featureCount']}**",
        f"- Unknown: **{summary['unknownCount']}**",
        f"- Missing implementation/scenario: **{summary['missingCount']}**",
        f"- Compatibility scenarios discovered: **{summary['scenarioCount']}**",
        "",
        "### Classification counts",
        "",
        "| Classification | Count |",
        "| --- | ---: |",
    ]
    for key, value in summary["byClassification"].items():
        lines.append(f"| {key} | {value} |")
    lines.extend([
        "",
        "## Inventory",
        "",
        "| ID | Line | Kind | Owner | Tags | iKeyd | Unit | Scenario | EXE diff | AHK diff | Real Win | Classification |",
        "| --- | ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ])
    for feature in report["features"]:
        c = feature["coverage"]
        lines.append(
            "| `{id}` | {line} | {kind} | {owner} | {tags} | {impl} | {unit} | {scenario} | "
            "{exe} | {ahk} | {real} | {classification} |".format(
                id=feature["id"],
                line=feature["line"],
                kind=feature["kind"],
                owner=feature["owner"].replace("|", "\\|"),
                tags=", ".join(feature["tags"]),
                impl=c["implementation"],
                unit=c["unit"],
                scenario=c["scenario"],
                exe=c["exeDiff"],
                ahk=c["ahkDiff"],
                real=c["realWindows"],
                classification=feature["classification"],
            )
        )
    lines.extend([
        "",
        "## Policy",
        "",
        "`unknown` and unintended `missing` entries are work remaining for #46. Coverage rules are deliberately conservative; evidence should be upgraded only when a test, differential observation, or real-Windows verification exists.",
        "",
    ])
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--coverage", type=Path)
    parser.add_argument("--profile", type=Path)
    parser.add_argument("--scenarios", type=Path)
    parser.add_argument("--json", dest="json_output", type=Path, required=True)
    parser.add_argument("--markdown", dest="markdown_output", type=Path, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    features = scan_source(args.source)
    apply_coverage(features, load_json(args.coverage, {}))
    scenarios = scenario_index(args.scenarios)
    report = build_report(args.source, features, scenarios, profile_summary(args.profile))
    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    args.markdown_output.parent.mkdir(parents=True, exist_ok=True)
    args.json_output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown_output.write_text(markdown(report), encoding="utf-8")
    summary = report["summary"]
    print(
        f"compatibility inventory: {summary['featureCount']} features, "
        f"{summary['unknownCount']} unknown, {summary['missingCount']} missing"
    )


if __name__ == "__main__":
    main()
