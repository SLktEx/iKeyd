#!/usr/bin/env python3
"""Compile the iKeyd authoring DSL to the static JSON profile IR."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import OrderedDict
from pathlib import Path

IDENT = r"[A-Za-z0-9_]+"
KEY_REF = rf"{IDENT}(?:\[\s*\d+\s*,\s*\d+\s*\])?"
MAX_BEHAVIOR_STATEMENT_DEPTH = 32


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


def parse_behavior_invocation(path: Path, lineno: int, value: str) -> dict[str, object] | None:
    value = value.strip().rstrip(";").strip()
    match = re.fullmatch(rf"({IDENT})\s*\((.*)\)", value)
    if not match:
        return None

    behavior_name = match.group(1)
    raw_arguments = match.group(2).strip()
    arguments: list[str] = []
    if raw_arguments:
        for argument in raw_arguments.split(","):
            token = argument.strip()
            if not re.fullmatch(IDENT, token):
                raise DslError(
                    path,
                    lineno,
                    f"behavior arguments must be identifiers in the current syntax: '{token}'",
                )
            arguments.append(token)

    return OrderedDict([
        ("name", behavior_name),
        ("arguments", arguments),
    ])


def parse_behavior_option_value(path: Path, lineno: int, value: str) -> str:
    value = value.strip().rstrip(";").strip()
    if not value:
        raise DslError(path, lineno, "behavior option value must not be empty")
    if value.startswith('"'):
        return parse_json_string(path, lineno, value)
    if not re.fullmatch(r"[-+A-Za-z0-9_.]+", value):
        raise DslError(path, lineno, f"invalid behavior option value '{value}'")
    return value


def parse_layout_row(path: Path, lineno: int, value: str) -> list[str]:
    value = value.strip().rstrip(";").strip()
    keys = [item for item in re.split(r"[\s,]+", value) if item]
    if not keys or not all(re.fullmatch(IDENT, key) for key in keys):
        raise DslError(path, lineno, "expected one or more key identifiers after 'row'")
    return keys


def resolve_key_ref(
    path: Path,
    lineno: int,
    value: str,
    layouts: dict[str, list[list[str]]],
) -> str:
    value = value.strip()
    direct = re.fullmatch(IDENT, value)
    if direct:
        return direct.group(0)

    coordinate = re.fullmatch(
        rf"({IDENT})\[\s*(\d+)\s*,\s*(\d+)\s*\]",
        value,
    )
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

    layout_row = layout[row - 1]
    if column > len(layout_row):
        raise DslError(path, lineno, f"column {column} is out of range for layout '{layout_name}' row {row}")
    return layout_row[column - 1]


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


def _brace_delta(line: str) -> int:
    return line.count("{") - line.count("}")


def _normalize_behavior_tokens(
    raw_lines: list[tuple[int, str]],
) -> list[tuple[int, str]]:
    result: list[tuple[int, str]] = []
    for lineno, raw in raw_lines:
        line = strip_comment(raw).strip()
        if not line:
            continue
        if re.fullmatch(r"}\s*else\s*{", line):
            result.append((lineno, "}"))
            result.append((lineno, "else {"))
        else:
            result.append((lineno, line))
    return result


def _parse_behavior_statements(
    path: Path,
    tokens: list[tuple[int, str]],
    index: int,
    depth: int,
) -> tuple[list[dict[str, object]], int]:
    if depth > MAX_BEHAVIOR_STATEMENT_DEPTH:
        line = tokens[index][0] if index < len(tokens) else 1
        raise DslError(path, line, "behavior statement nesting is too deep")

    statements: list[dict[str, object]] = []
    while index < len(tokens):
        lineno, line = tokens[index]
        line = line.rstrip(";").strip()
        if line == "}":
            return statements, index + 1

        if_match = re.fullmatch(rf"if\s+({IDENT})\s*\{{", line)
        if if_match:
            then_statements, index = _parse_behavior_statements(path, tokens, index + 1, depth + 1)
            else_statements: list[dict[str, object]] = []
            if index < len(tokens) and tokens[index][1].rstrip(";").strip() == "else {":
                else_statements, index = _parse_behavior_statements(path, tokens, index + 1, depth + 1)
            statements.append(OrderedDict([
                ("op", "if_bool"),
                ("condition", if_match.group(1)),
                ("then", then_statements),
                ("else", else_statements),
            ]))
            continue

        assign = re.fullmatch(rf"({IDENT})\s*=\s*(true|false)", line, re.IGNORECASE)
        if assign:
            statements.append(OrderedDict([
                ("op", "set_bool"),
                ("target", assign.group(1)),
                ("value", assign.group(2).lower()),
            ]))
            index += 1
            continue

        send = re.fullmatch(rf"send\s+({IDENT})", line)
        if send:
            statements.append(OrderedDict([("op", "send"), ("value", send.group(1))]))
            index += 1
            continue

        action = re.fullmatch(
            rf"(layer\.on|layer\.off|modifier\.down|modifier\.up)\s*\(\s*({IDENT})\s*\)",
            line,
        )
        if action:
            op = {
                "layer.on": "layer_on",
                "layer.off": "layer_off",
                "modifier.down": "modifier_down",
                "modifier.up": "modifier_up",
            }[action.group(1)]
            statements.append(OrderedDict([("op", op), ("value", action.group(2))]))
            index += 1
            continue

        raise DslError(path, lineno, f"unsupported behavior statement: {line}")

    line = tokens[-1][0] if tokens else 1
    raise DslError(path, line, "unclosed behavior statement block")


def _parse_user_behavior_definition(
    path: Path,
    name: str,
    parameters: list[str],
    raw_lines: list[tuple[int, str]],
) -> dict[str, object]:
    tokens = _normalize_behavior_tokens(raw_lines)
    locals_map: OrderedDict[str, bool] = OrderedDict()
    handlers: OrderedDict[str, dict[str, object]] = OrderedDict()
    index = 0

    while index < len(tokens):
        lineno, line = tokens[index]
        line = line.rstrip(";").strip()
        local = re.fullmatch(rf"var\s+({IDENT})\s*:\s*bool\s*=\s*(true|false)", line, re.IGNORECASE)
        if local:
            local_name = local.group(1)
            if local_name in locals_map or any(local_name.casefold() == p.casefold() for p in parameters):
                raise DslError(path, lineno, f"duplicate/conflicting behavior local '{local_name}'")
            locals_map[local_name] = local.group(2).lower() == "true"
            index += 1
            continue

        handler = re.fullmatch(rf"on_(press|release)\s*\{{", line)
        handler_params: list[str] = []
        event_name: str | None = None
        if handler:
            event_name = handler.group(1)
        else:
            interrupt = re.fullmatch(rf"on_interrupt\s*\(\s*({IDENT})\s*\)\s*\{{", line)
            if interrupt:
                event_name = "interrupt"
                handler_params = [interrupt.group(1)]

        if event_name is not None:
            if event_name in handlers:
                raise DslError(path, lineno, f"duplicate behavior handler 'on_{event_name}'")
            statements, index = _parse_behavior_statements(path, tokens, index + 1, 0)
            handlers[event_name] = OrderedDict([
                ("parameters", handler_params),
                ("statements", statements),
            ])
            continue

        raise DslError(path, lineno, f"unsupported behavior declaration: {line}")

    return OrderedDict([
        ("parameters", parameters),
        ("locals", locals_map),
        ("handlers", handlers),
    ])


def extract_user_behaviors(
    text: str,
    path: Path,
) -> tuple[str, OrderedDict[str, dict[str, object]]]:
    lines = text.splitlines()
    definitions: OrderedDict[str, dict[str, object]] = OrderedDict()
    output = list(lines)
    index = 0

    while index < len(lines):
        line = strip_comment(lines[index]).strip()
        header = re.fullmatch(rf"behavior\s+({IDENT})\s*\(([^)]*)\)\s*\{{", line)
        if not header:
            index += 1
            continue

        name = header.group(1)
        if name in definitions:
            raise DslError(path, index + 1, f"duplicate behavior definition '{name}'")
        raw_parameters = header.group(2).strip()
        parameters: list[str] = []
        if raw_parameters:
            for parameter in raw_parameters.split(","):
                token = parameter.strip()
                if not re.fullmatch(IDENT, token):
                    raise DslError(path, index + 1, f"invalid behavior parameter '{token}'")
                if any(token.casefold() == existing.casefold() for existing in parameters):
                    raise DslError(path, index + 1, f"duplicate behavior parameter '{token}'")
                parameters.append(token)

        depth = _brace_delta(line)
        body: list[tuple[int, str]] = []
        output[index] = ""
        cursor = index + 1
        while cursor < len(lines) and depth > 0:
            current = strip_comment(lines[cursor])
            depth += _brace_delta(current)
            output[cursor] = ""
            if depth > 0:
                body.append((cursor + 1, current))
            cursor += 1
        if depth != 0:
            raise DslError(path, index + 1, f"unclosed behavior '{name}'")

        definitions[name] = _parse_user_behavior_definition(path, name, parameters, body)
        index = cursor

    return "\n".join(output), definitions


def compile_dsl(text: str, path: Path) -> dict[str, object]:
    text, user_behavior_definitions = extract_user_behaviors(text, path)
    source: OrderedDict[str, object] = OrderedDict()
    layouts: OrderedDict[str, list[list[str]]] = OrderedDict()
    single: OrderedDict[str, OrderedDict[str, str]] = OrderedDict()
    chords: OrderedDict[str, list[list[str]]] = OrderedDict()
    behaviors: OrderedDict[str, OrderedDict[str, dict[str, object]]] = OrderedDict()
    duplicate_flags: list[dict[str, object]] = []

    block: tuple[str, str | None] | None = None
    parent_block: tuple[str, str | None] | None = None
    pending_behavior: tuple[str, str] | None = None
    saw_profile = False

    for lineno, raw in enumerate(text.splitlines(), 1):
        line = strip_comment(raw).strip()
        if not line:
            continue

        if line == "}":
            if block is None:
                raise DslError(path, lineno, "unexpected '}'")
            if block[0] == "behavior_options":
                block = parent_block
                parent_block = None
                pending_behavior = None
            else:
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

            keymap = re.fullmatch(rf"keymap\s+({IDENT})\s*\{{", line)
            if keymap:
                mode = keymap.group(1)
                if mode in single:
                    raise DslError(path, lineno, f"duplicate keymap '{mode}'")
                single[mode] = OrderedDict()
                chords[mode] = []
                behaviors[mode] = OrderedDict()
                block = ("keymap", mode)
                continue

            if re.fullmatch(r"quirks\s*\{", line):
                block = ("quirks", None)
                continue

            raise DslError(path, lineno, f"unexpected top-level statement: {line}")

        kind, name = block
        if kind == "behavior_options":
            if pending_behavior is None:
                raise DslError(path, lineno, "internal behavior-options state is missing")
            mode, key = pending_behavior
            match = re.fullmatch(rf"({IDENT})\s*=\s*(.+)", line)
            if not match:
                raise DslError(path, lineno, f"unknown behavior option statement: {line}")
            option_name = match.group(1)
            options = behaviors[mode][key].setdefault("options", OrderedDict())
            assert isinstance(options, OrderedDict)
            if option_name in options:
                raise DslError(path, lineno, f"duplicate behavior option '{option_name}'")
            options[option_name] = parse_behavior_option_value(path, lineno, match.group(2))
            continue

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
            if match:
                row = parse_layout_row(path, lineno, match.group(1))
                existing_keys = {key.casefold() for existing in layouts[name] for key in existing}
                duplicate = next((key for key in row if key.casefold() in existing_keys), None)
                if duplicate is not None:
                    raise DslError(path, lineno, f"duplicate key '{duplicate}' in layout '{name}'")
                row_seen: set[str] = set()
                duplicate_in_row = None
                for key in row:
                    folded = key.casefold()
                    if folded in row_seen:
                        duplicate_in_row = key
                        break
                    row_seen.add(folded)
                if duplicate_in_row is not None:
                    raise DslError(path, lineno, f"duplicate key '{duplicate_in_row}' in layout '{name}'")
                layouts[name].append(row)
                continue
            raise DslError(path, lineno, f"unknown layout statement: {line}")

        if kind == "keymap":
            assert name is not None
            match = re.fullmatch(rf"combo\s+({KEY_REF})\s*\+\s*({KEY_REF})\s*=\s*(.+)", line)
            if match:
                first = resolve_key_ref(path, lineno, match.group(1), layouts)
                second = resolve_key_ref(path, lineno, match.group(2), layouts)
                chords[name].append([first, second, parse_json_string(path, lineno, match.group(3))])
                continue

            option_block = re.fullmatch(rf"({KEY_REF})\s*=\s*(.+?)\s*\{{", line)
            if option_block:
                key = resolve_key_ref(path, lineno, option_block.group(1), layouts)
                if key in single[name] or key in behaviors[name]:
                    raise DslError(path, lineno, f"duplicate key mapping '{name}.{key}'")
                invocation = parse_behavior_invocation(path, lineno, option_block.group(2))
                if invocation is None:
                    raise DslError(path, lineno, "option blocks are only valid for behavior invocations")
                invocation["options"] = OrderedDict()
                behaviors[name][key] = invocation
                parent_block = block
                pending_behavior = (name, key)
                block = ("behavior_options", name)
                continue

            match = re.fullmatch(rf"({KEY_REF})\s*=\s*(.+)", line)
            if match:
                key = resolve_key_ref(path, lineno, match.group(1), layouts)
                if key in single[name] or key in behaviors[name]:
                    raise DslError(path, lineno, f"duplicate key mapping '{name}.{key}'")

                value = match.group(2)
                invocation = parse_behavior_invocation(path, lineno, value)
                if invocation is not None:
                    behaviors[name][key] = invocation
                else:
                    single[name][key] = parse_json_string(path, lineno, value)
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

    result = OrderedDict([
        ("source", source),
        ("singleStroke", single),
        ("chords", chords),
    ])
    if any(behaviors_by_mode for behaviors_by_mode in behaviors.values()):
        result["behaviors"] = behaviors
    if user_behavior_definitions:
        result["behaviorDefinitions"] = user_behavior_definitions
    result["knownQuirks"] = OrderedDict([
        ("duplicateChordPatterns", duplicate_chord_metadata(chords)),
        ("duplicateFlagDefinitions", duplicate_flags),
    ])
    return result


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
