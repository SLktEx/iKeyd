#!/usr/bin/env python3
"""Compile the small iKeyd authoring DSL prototype to the existing JSON profile schema."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import OrderedDict
from pathlib import Path

IDENT = r"[A-Za-z0-9_]+"


class DslError(ValueError):
    def __init__(self, path: Path, line: int, message: str):
        super().__init__(f"{path}:{line}: {message}")


def strip_comment(line: str) -> str:
    in_string = False
    escaped = False
    i = 0
    while i < len(line):
        ch = line[i]
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
        else:
            if ch == '"':
                in_string = True
            elif ch == "/" and i + 1 < len(line) and line[i + 1] == "/":
                return line[:i]
        i += 1
    return line


def parse_json_string(path: Path, lineno: int, value: str) -> str:
    value = value.strip().rstrip(";").strip()
    try:
        parsed = json.loads(value)
    except json.JSONDecodeError as exc:
        raise DslError(path, lineno, f"expected a quoted string: {exc.msg}") from exc
    if not isinstance(parsed, str):
        raise DslError(path, lineno, "expected a quoted string")
    return parsed


def parse_string_list(path: Path, lineno: int, value: str) -> list[str]:
    value = value.strip().rstrip(";").strip()
    try:
        parsed = json.loads(f"[{value}]")
    except json.JSONDecodeError as exc:
        raise DslError(path, lineno, f"expected comma-separated quoted strings: {exc.msg}") from exc
    if not parsed or not all(isinstance(item, str) for item in parsed):
        raise DslError(path, lineno, "expected one or more quoted strings")
    return parsed


def canonical_pair(first: str, second: str) -> tuple[str, str]:
    return tuple(sorted((first.casefold(), second.casefold())))


def duplicate_chord_metadata(chords: dict[str, list[list[str]]]) -> dict[str, list[dict[str, object]]]:
    result: dict[str, list[dict[str, object]]] = {}
    for mode, entries in chords.items():
        seen: OrderedDict[tuple[str, str], list[str]] = OrderedDict()
        for first, second, output in entries:
            seen.setdefault(canonical_pair(first, second), []).append(output)

        duplicates = []
        for keys, outputs in seen.items():
            if len(outputs) > 1:
                duplicates.append({
                    "keys": list(keys),
                    "outputs": outputs,
                    "effectiveOutput": outputs[0],
                })
        result[mode] = duplicates
    return result


def compile_dsl(text: str, path: Path) -> dict[str, object]:
    source: OrderedDict[str, object] = OrderedDict()
    single: OrderedDict[str, OrderedDict[str, str]] = OrderedDict()
    chords: OrderedDict[str, list[list[str]]] = OrderedDict()
    duplicate_flags: list[dict[str, object]] = []

    block: tuple[str, str | None] | None = None
    saw_profile = False

    for lineno, raw in enumerate(text.splitlines(), 1):
        line = strip_comment(raw).strip()
        if not line:
            continue

        if line == "}":
            if block is None:
                raise DslError(path, lineno, "unexpected '}'")
            block = None
            continue

        if block is None:
            profile = re.fullmatch(rf"profile\s+({IDENT})\s*\{{", line)
            if profile:
                if saw_profile:
                    raise DslError(path, lineno, "only one profile block is allowed")
                saw_profile = True
                block = ("profile", profile.group(1))
                continue

            keymap = re.fullmatch(rf"keymap\s+({IDENT})\s*\{{", line)
            if keymap:
                mode = keymap.group(1)
                if mode in single:
                    raise DslError(path, lineno, f"duplicate keymap '{mode}'")
                single[mode] = OrderedDict()
                chords[mode] = []
                block = ("keymap", mode)
                continue

            if re.fullmatch(r"quirks\s*\{", line):
                block = ("quirks", None)
                continue

            raise DslError(path, lineno, f"unexpected top-level statement: {line}")

        kind, name = block
        if kind == "profile":
            match = re.fullmatch(r"runtime\s*=\s*(.+)", line)
            if match:
                source["runtime"] = parse_json_string(path, lineno, match.group(1))
                continue
            match = re.fullmatch(r"executable_lines\s*=\s*(\d+)\s*;?", line)
            if match:
                source["executableLines"] = int(match.group(1))
                continue
            match = re.fullmatch(r"chord_window\s*=\s*(\d+)\s*ms\s*;?", line)
            if match:
                source["chordWindowMs"] = int(match.group(1))
                continue
            raise DslError(path, lineno, f"unknown profile setting: {line}")

        if kind == "keymap":
            assert name is not None
            match = re.fullmatch(rf"combo\s+({IDENT})\s*\+\s*({IDENT})\s*=\s*(.+)", line)
            if match:
                chords[name].append([
                    match.group(1),
                    match.group(2),
                    parse_json_string(path, lineno, match.group(3)),
                ])
                continue

            match = re.fullmatch(rf"({IDENT})\s*=\s*(.+)", line)
            if match:
                key = match.group(1)
                if key in single[name]:
                    raise DslError(path, lineno, f"duplicate single-stroke mapping '{name}.{key}'")
                single[name][key] = parse_json_string(path, lineno, match.group(2))
                continue

            raise DslError(path, lineno, f"unknown keymap statement: {line}")

        if kind == "quirks":
            match = re.fullmatch(rf"duplicate_flag\s+({IDENT})\s*=\s*(.+)", line)
            if match:
                duplicate_flags.append({
                    "key": match.group(1),
                    "expressions": parse_string_list(path, lineno, match.group(2)),
                })
                continue
            raise DslError(path, lineno, f"unknown quirks statement: {line}")

    if block is not None:
        raise DslError(path, len(text.splitlines()), f"unclosed {block[0]} block")
    if not saw_profile:
        raise DslError(path, 1, "profile block is required")
    if "chordWindowMs" not in source:
        raise DslError(path, 1, "profile.chord_window is required")
    if not single:
        raise DslError(path, 1, "at least one keymap is required")

    return OrderedDict([
        ("source", source),
        ("singleStroke", single),
        ("chords", chords),
        ("knownQuirks", OrderedDict([
            ("duplicateChordPatterns", duplicate_chord_metadata(chords)),
            ("duplicateFlagDefinitions", duplicate_flags),
        ])),
    ])


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compile iKeyd DSL to the existing JSON profile schema.")
    parser.add_argument("input", type=Path, help="input .ikeyd file")
    parser.add_argument("output", type=Path, help="output .json file")
    parser.add_argument(
        "--check-against",
        type=Path,
        help="fail unless generated JSON is semantically equal to this file",
    )
    args = parser.parse_args(argv)

    try:
        profile = compile_dsl(args.input.read_text(encoding="utf-8"), args.input)
        if args.check_against:
            expected = json.loads(args.check_against.read_text(encoding="utf-8"))
            if profile != expected:
                print(f"{args.input}: generated profile differs from {args.check_against}", file=sys.stderr)
                return 1

        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(profile, ensure_ascii=False, separators=(",", ":")),
            encoding="utf-8",
        )
        return 0
    except (OSError, DslError, json.JSONDecodeError) as exc:
        print(f"iKeyd DSL compilation failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
