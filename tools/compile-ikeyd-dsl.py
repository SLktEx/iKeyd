#!/usr/bin/env python3
"""Compile the iKeyd authoring DSL to the existing JSON profile schema."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import OrderedDict
from pathlib import Path

IDENT = r"[A-Za-z0-9_]+"
KEY_REF = rf"{IDENT}(?:\[\s*\d+\s*,\s*\d+\s*\])?"


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


def parse_layout_row(path: Path, lineno: int, value: str) -> list[str]:
    value = value.strip().rstrip(";").strip()
    keys = [item for item in re.split(r"[\s,]+", value) if item]
    if not keys or not all(re.fullmatch(IDENT, key) for key in keys):
        raise DslError(path, lineno, "expected one or more key identifiers after 'row'")
    return keys


def resolve_key_ref(path: Path, lineno: int, value: str, layouts: dict[str, list[list[str]]]) -> str:
    value = value.strip()
    direct = re.fullmatch(IDENT, value)
    if direct:
        return direct.group(0)

    coordinate = re.fullmatch(rf"({IDENT})\[\s*(\d+)\s*,\s*(\d+)\s*\]", value)
    if not coordinate:
        raise DslError(path, lineno, f"invalid key reference '{value}'")

    layout_name = coordinate.group(1)
    row = int(coordinate.group(2))
    column = int(coordinate.group(3))
    if row < 1 or column < 1:
        raise DslError(path, lineno, f"key positions are 1-based: '{value}'")

    resolved_layout_name = layout_name
    if layout_name == "POS" and "POS" not in layouts and "BASE" in layouts:
        resolved_layout_name = "BASE"

    layout = layouts.get(resolved_layout_name)
    if layout is None:
        raise DslError(path, lineno, f"unknown layout '{layout_name}' in key reference '{value}'")
    if row > len(layout):
        raise DslError(path, lineno, f"row {row} is out of range for layout '{layout_name}'")
    if column > len(layout[row - 1]):
        raise DslError(path, lineno, f"column {column} is out of range for layout '{layout_name}' row {row}")
    return layout[row - 1][column - 1]


def canonical_pair(first: str, second: str) -> tuple[str, str]:
    return tuple(sorted((first.casefold(), second.casefold())))


def duplicate_chord_metadata(chords: dict[str, list[list[str]]]) -> dict[str, list[dict[str, object]]]:
    result: dict[str, list[dict[str, object]]] = {}
    for mode, entries in chords.items():
        seen: OrderedDict[tuple[str, str], list[str]] = OrderedDict()
        for first, second, output in entries:
            seen.setdefault(canonical_pair(first, second), []).append(output)
        result[mode] = [
            {"keys": list(keys), "outputs": outputs, "effectiveOutput": outputs[0]}
            for keys, outputs in seen.items()
            if len(outputs) > 1
        ]
    return result


def add_single(path: Path, lineno: int, mode: str, key: str, output: str, single) -> None:
    if any(existing.casefold() == key.casefold() for existing in single[mode]):
        raise DslError(path, lineno, f"duplicate single-stroke mapping '{mode}.{key}'")
    single[mode][key] = output


def add_chord(path: Path, lineno: int, mode: str, first: str, second: str, output: str, chords) -> None:
    if first.casefold() == second.casefold():
        raise DslError(path, lineno, f"combo cannot use the same key twice: '{first}'")
    chords[mode].append([first, second, output])


def compile_dsl(text: str, path: Path) -> dict[str, object]:
    source: OrderedDict[str, object] = OrderedDict()
    layouts: OrderedDict[str, list[list[str]]] = OrderedDict()
    single: OrderedDict[str, OrderedDict[str, str]] = OrderedDict()
    chords: OrderedDict[str, list[list[str]]] = OrderedDict()
    duplicate_flags: list[dict[str, object]] = []
    keymap_layouts: dict[str, str | None] = {}

    block: tuple[str, str | None] | None = None
    section: tuple[str, str | None] | None = None
    map_row_index = 0
    saw_profile = False
    lines = text.splitlines()

    for lineno, raw in enumerate(lines, 1):
        line = strip_comment(raw).strip()
        if not line:
            continue

        if line == "}":
            if section is not None:
                if section[0] == "map":
                    assert block and block[1]
                    mode = block[1]
                    layout_name = keymap_layouts[mode]
                    assert layout_name is not None
                    expected_rows = len(layouts[layout_name])
                    if map_row_index != expected_rows:
                        raise DslError(path, lineno, f"map for keymap '{mode}' has {map_row_index} rows; layout '{layout_name}' has {expected_rows}")
                section = None
                map_row_index = 0
                continue
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

            layout = re.fullmatch(rf"layout\s+({IDENT})\s*\{{", line)
            if layout:
                layout_name = layout.group(1)
                if layout_name in layouts:
                    raise DslError(path, lineno, f"duplicate layout '{layout_name}'")
                layouts[layout_name] = []
                block = ("layout", layout_name)
                continue

            keymap = re.fullmatch(rf"keymap\s+({IDENT})(?:\s+using\s+({IDENT}))?\s*\{{", line)
            if keymap:
                mode, layout_name = keymap.group(1), keymap.group(2)
                if mode in single:
                    raise DslError(path, lineno, f"duplicate keymap '{mode}'")
                if layout_name is not None and layout_name not in layouts:
                    raise DslError(path, lineno, f"unknown layout '{layout_name}'")
                single[mode] = OrderedDict()
                chords[mode] = []
                keymap_layouts[mode] = layout_name
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

        if kind == "layout":
            assert name is not None
            match = re.fullmatch(r"row\s+(.+)", line)
            if not match:
                raise DslError(path, lineno, f"unknown layout statement: {line}")
            row = parse_layout_row(path, lineno, match.group(1))
            seen = {key.casefold() for existing in layouts[name] for key in existing}
            row_seen: set[str] = set()
            for key in row:
                folded = key.casefold()
                if folded in seen or folded in row_seen:
                    raise DslError(path, lineno, f"duplicate key '{key}' in layout '{name}'")
                row_seen.add(folded)
            layouts[name].append(row)
            continue

        if kind == "keymap":
            assert name is not None
            if section is not None:
                section_kind, section_arg = section
                if section_kind == "map":
                    layout_name = keymap_layouts[name]
                    assert layout_name is not None
                    layout = layouts[layout_name]
                    match = re.fullmatch(r"row\s+(.+)", line)
                    if not match:
                        raise DslError(path, lineno, f"unknown map statement: {line}")
                    if map_row_index >= len(layout):
                        raise DslError(path, lineno, f"too many rows in map for keymap '{name}'")
                    outputs = parse_string_list(path, lineno, match.group(1))
                    keys = layout[map_row_index]
                    if len(outputs) != len(keys):
                        raise DslError(path, lineno, f"map row {map_row_index + 1} has {len(outputs)} outputs; layout '{layout_name}' row has {len(keys)} keys")
                    for key, output in zip(keys, outputs):
                        add_single(path, lineno, name, key, output, single)
                    map_row_index += 1
                    continue

                if section_kind == "combos":
                    assert section_arg is not None
                    match = re.fullmatch(rf"({KEY_REF})\s*=\s*(.+)", line)
                    if not match:
                        raise DslError(path, lineno, f"unknown combos statement: {line}")
                    second = resolve_key_ref(path, lineno, match.group(1), layouts)
                    add_chord(path, lineno, name, section_arg, second, parse_json_string(path, lineno, match.group(2)), chords)
                    continue

            if re.fullmatch(r"map\s*\{", line):
                layout_name = keymap_layouts[name]
                if layout_name is None:
                    raise DslError(path, lineno, f"keymap '{name}' must declare 'using <layout>' before map {{")
                section = ("map", layout_name)
                map_row_index = 0
                continue

            combo_group = re.fullmatch(rf"combos\s+({KEY_REF})\s*\{{", line)
            if combo_group:
                section = ("combos", resolve_key_ref(path, lineno, combo_group.group(1), layouts))
                continue

            match = re.fullmatch(rf"combo\s+({KEY_REF})\s*\+\s*({KEY_REF})\s*=\s*(.+)", line)
            if match:
                first = resolve_key_ref(path, lineno, match.group(1), layouts)
                second = resolve_key_ref(path, lineno, match.group(2), layouts)
                add_chord(path, lineno, name, first, second, parse_json_string(path, lineno, match.group(3)), chords)
                continue

            match = re.fullmatch(rf"({KEY_REF})\s*=\s*(.+)", line)
            if match:
                key = resolve_key_ref(path, lineno, match.group(1), layouts)
                add_single(path, lineno, name, key, parse_json_string(path, lineno, match.group(2)), single)
                continue
            raise DslError(path, lineno, f"unknown keymap statement: {line}")

        if kind == "quirks":
            match = re.fullmatch(rf"duplicate_flag\s+({IDENT})\s*=\s*(.+)", line)
            if match:
                duplicate_flags.append({"key": match.group(1), "expressions": parse_string_list(path, lineno, match.group(2))})
                continue
            raise DslError(path, lineno, f"unknown quirks statement: {line}")

    if section is not None:
        raise DslError(path, len(lines), f"unclosed {section[0]} section")
    if block is not None:
        raise DslError(path, len(lines), f"unclosed {block[0]} block")
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
    parser.add_argument("--check-against", type=Path, help="fail unless generated JSON is semantically equal to this file")
    args = parser.parse_args(argv)

    try:
        profile = compile_dsl(args.input.read_text(encoding="utf-8"), args.input)
        if args.check_against:
            expected = json.loads(args.check_against.read_text(encoding="utf-8"))
            if profile != expected:
                print(f"{args.input}: generated profile differs from {args.check_against}", file=sys.stderr)
                return 1
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(profile, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        return 0
    except (OSError, DslError, json.JSONDecodeError) as exc:
        print(f"iKeyd DSL compilation failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())