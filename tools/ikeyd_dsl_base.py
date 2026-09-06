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
KEY_REF = rf"{IDENT}(?:\[\s*\d+\s*,\s*\d+\s*\]|\.{IDENT})?"

# OADG-style JIS 109 physical keyboard. NumpadComma is intentionally not part
# of the 109-key preset even though iKeyd supports it as an extra compact key.
JIS109_LAYOUT: list[list[str]] = [
    ["Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PrintScreen", "ScrollLock", "Pause"],
    ["ZenkakuHankaku", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Minus", "Caret", "Yen", "Backspace"],
    ["Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "AT", "LeftBracket"],
    ["CapsLock", "A", "S", "D", "F", "G", "H", "J", "K", "L", "SColon", "Colon", "RightBracket", "Enter"],
    ["LeftShift", "Z", "X", "C", "V", "B", "N", "M", "Comma", "Dot", "Slash", "Ro", "RightShift"],
    ["LeftControl", "LeftGui", "LeftAlt", "Muhenkan", "Space", "Henkan", "KatakanaHiragana", "RightAlt", "RightGui", "Menu", "RightControl"],
    ["Insert", "Home", "PageUp"],
    ["Delete", "End", "PageDown"],
    ["Left", "Up", "Down", "Right"],
    ["NumLock", "NumpadSlash", "NumpadAsterisk", "NumpadMinus"],
    ["Numpad7", "Numpad8", "Numpad9", "NumpadPlus"],
    ["Numpad4", "Numpad5", "Numpad6"],
    ["Numpad1", "Numpad2", "Numpad3", "NumpadEnter"],
    ["Numpad0", "NumpadDot"],
]

BUILTIN_KEYBOARDS: dict[str, list[list[str]]] = {"JIS109": JIS109_LAYOUT}


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


def parse_output(path: Path, lineno: int, value: str) -> str:
    value = value.strip().rstrip(";").strip()
    if not value:
        raise DslError(path, lineno, "expected an output value")
    if value.startswith('"'):
        return parse_json_string(path, lineno, value)
    if any(ch.isspace() for ch in value):
        raise DslError(path, lineno, "outputs containing whitespace must be quoted")
    return value


def parse_output_row(path: Path, lineno: int, value: str) -> list[str]:
    value = value.strip().rstrip(";").strip()
    if not value:
        raise DslError(path, lineno, "expected one or more output values after 'row'")

    decoder = json.JSONDecoder()
    outputs: list[str] = []
    i = 0
    while i < len(value):
        while i < len(value) and (value[i].isspace() or value[i] == ","):
            i += 1
        if i >= len(value):
            break

        if value[i] == '"':
            try:
                parsed, consumed = decoder.raw_decode(value[i:])
            except json.JSONDecodeError as exc:
                raise DslError(path, lineno, f"invalid quoted output: {exc.msg}") from exc
            if not isinstance(parsed, str):
                raise DslError(path, lineno, "quoted row outputs must be strings")
            outputs.append(parsed)
            i += consumed
            continue

        start = i
        while i < len(value) and not value[i].isspace():
            i += 1
        token = value[start:i]
        if token:
            outputs.append(token)

    if not outputs:
        raise DslError(path, lineno, "expected one or more output values after 'row'")
    return outputs


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


def find_layout(layouts: dict[str, list[list[str]]], name: str) -> tuple[str, list[list[str]]] | None:
    for existing_name, layout in layouts.items():
        if existing_name.casefold() == name.casefold():
            return existing_name, layout
    return None


def resolve_pos_layout(layouts: dict[str, list[list[str]]]) -> str | None:
    if find_layout(layouts, "POS") is not None:
        return "POS"
    if find_layout(layouts, "JIS109") is not None:
        return "JIS109"
    if find_layout(layouts, "BASE") is not None:
        return "BASE"
    return None


def resolve_key_ref(path: Path, lineno: int, value: str, layouts: dict[str, list[list[str]]]) -> str:
    value = value.strip()
    direct = re.fullmatch(IDENT, value)
    if direct:
        return direct.group(0)

    named = re.fullmatch(rf"({IDENT})\.({IDENT})", value)
    if named:
        layout_name, requested_key = named.group(1), named.group(2)
        resolved_layout_name = layout_name
        if layout_name.casefold() == "pos":
            pos_layout = resolve_pos_layout(layouts)
            if pos_layout is not None:
                resolved_layout_name = pos_layout
        resolved = find_layout(layouts, resolved_layout_name)
        if resolved is None:
            raise DslError(path, lineno, f"unknown layout '{layout_name}' in key reference '{value}'")
        _, layout = resolved
        for key in (key for row in layout for key in row):
            if key.casefold() == requested_key.casefold():
                return key
        raise DslError(path, lineno, f"layout '{layout_name}' has no key named '{requested_key}'")

    coordinate = re.fullmatch(rf"({IDENT})\[\s*(\d+)\s*,\s*(\d+)\s*\]", value)
    if not coordinate:
        raise DslError(path, lineno, f"invalid key reference '{value}'")

    layout_name = coordinate.group(1)
    row = int(coordinate.group(2))
    column = int(coordinate.group(3))
    if row < 1 or column < 1:
        raise DslError(path, lineno, f"key positions are 1-based: '{value}'")

    resolved_layout_name = layout_name
    if layout_name.casefold() == "pos":
        pos_layout = resolve_pos_layout(layouts)
        if pos_layout is not None:
            resolved_layout_name = pos_layout

    resolved = find_layout(layouts, resolved_layout_name)
    if resolved is None:
        raise DslError(path, lineno, f"unknown layout '{layout_name}' in key reference '{value}'")
    _, layout = resolved
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
    keyboard_preset: str | None = None
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
                    resolved = find_layout(layouts, layout_name)
                    assert resolved is not None
                    expected_rows = len(resolved[1])
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

            keyboard = re.fullmatch(rf"keyboard\s+({IDENT})\s*;?", line)
            if keyboard:
                requested = keyboard.group(1)
                if keyboard_preset is not None:
                    raise DslError(path, lineno, f"keyboard preset already declared as '{keyboard_preset}'")
                canonical = next((name for name in BUILTIN_KEYBOARDS if name.casefold() == requested.casefold()), None)
                if canonical is None:
                    raise DslError(path, lineno, f"unknown keyboard preset '{requested}'")
                if find_layout(layouts, canonical) is not None:
                    raise DslError(path, lineno, f"layout '{canonical}' is already defined")
                layouts[canonical] = [row.copy() for row in BUILTIN_KEYBOARDS[canonical]]
                keyboard_preset = canonical
                continue

            layout = re.fullmatch(rf"layout\s+({IDENT})\s*\{{", line)
            if layout:
                layout_name = layout.group(1)
                if find_layout(layouts, layout_name) is not None:
                    raise DslError(path, lineno, f"duplicate layout '{layout_name}'")
                layouts[layout_name] = []
                block = ("layout", layout_name)
                continue

            keymap = re.fullmatch(rf"keymap\s+({IDENT})(?:\s+using\s+({IDENT}))?\s*\{{", line)
            if keymap:
                mode, layout_name = keymap.group(1), keymap.group(2)
                if mode in single:
                    raise DslError(path, lineno, f"duplicate keymap '{mode}'")
                if layout_name is not None and find_layout(layouts, layout_name) is None:
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
                    resolved = find_layout(layouts, layout_name)
                    assert resolved is not None
                    layout = resolved[1]
                    match = re.fullmatch(r"row\s+(.+)", line)
                    if not match:
                        raise DslError(path, lineno, f"unknown map statement: {line}")
                    if map_row_index >= len(layout):
                        raise DslError(path, lineno, f"too many rows in map for keymap '{name}'")
                    outputs = parse_output_row(path, lineno, match.group(1))
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
                    add_chord(path, lineno, name, section_arg, second, parse_output(path, lineno, match.group(2)), chords)
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
                add_chord(path, lineno, name, first, second, parse_output(path, lineno, match.group(3)), chords)
                continue

            match = re.fullmatch(rf"({KEY_REF})\s*=\s*(.+)", line)
            if match:
                key = resolve_key_ref(path, lineno, match.group(1), layouts)
                add_single(path, lineno, name, key, parse_output(path, lineno, match.group(2)), single)
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