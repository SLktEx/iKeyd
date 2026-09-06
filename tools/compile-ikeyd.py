#!/usr/bin/env python3
"""Compile the iKeyd authoring DSL, including top-level mouse settings."""

from __future__ import annotations

import argparse
import importlib.util
import json
import re
import sys
from collections import OrderedDict
from pathlib import Path

LEGACY_COMPILER = Path(__file__).with_name("compile-ikeyd-dsl.py")
spec = importlib.util.spec_from_file_location("ikeyd_dsl_core", LEGACY_COMPILER)
_core = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = _core
spec.loader.exec_module(_core)

DslError = _core.DslError


def _token(path: Path, lineno: int, value: str, setting: str) -> str:
    token = value.strip().rstrip(";").strip().lower()
    if not re.fullmatch(r"[a-z0-9_-]+", token):
        raise DslError(path, lineno, f"invalid mouse.{setting} value '{value.strip()}'")
    return token


def _duration_ms(path: Path, lineno: int, value: str, setting: str, *, positive: bool) -> int:
    token = value.strip().rstrip(";").strip()
    match = re.fullmatch(r"(\d+)\s*ms", token, re.IGNORECASE)
    if not match:
        raise DslError(path, lineno, f"mouse.{setting} must be a duration such as 8ms")
    result = int(match.group(1))
    if positive and result <= 0:
        raise DslError(path, lineno, f"mouse.{setting} must be greater than 0ms")
    return result


def _speed(path: Path, lineno: int, value: str, setting: str) -> float | int:
    token = value.strip().rstrip(";").strip()
    match = re.fullmatch(r"(\d+(?:\.\d+)?)\s*(?:px/s)?", token, re.IGNORECASE)
    if not match:
        raise DslError(path, lineno, f"mouse.speed.{setting} must be a non-negative number or px/s value")
    number = float(match.group(1))
    return int(number) if number.is_integer() else number


def _pixels(path: Path, lineno: int, value: str, setting: str) -> int:
    token = value.strip().rstrip(";").strip()
    match = re.fullmatch(r"(\d+)\s*px", token, re.IGNORECASE)
    if not match:
        raise DslError(path, lineno, f"mouse.{setting} must be a pixel value such as 1px")
    return int(match.group(1))


def _find_mouse_block(text: str, path: Path) -> tuple[list[str], int, int] | None:
    lines = text.splitlines()
    depth = 0
    found: tuple[int, int] | None = None

    for index, raw in enumerate(lines):
        line = _core.strip_comment(raw).strip()
        if depth == 0 and re.fullmatch(r"mouse\s*\{", line):
            if found is not None:
                raise DslError(path, index + 1, "only one mouse block is allowed")
            start = index
            block_depth = _core._brace_delta(line)
            cursor = index + 1
            while cursor < len(lines) and block_depth > 0:
                block_depth += _core._brace_delta(_core.strip_comment(lines[cursor]))
                cursor += 1
            if block_depth != 0:
                raise DslError(path, index + 1, "unclosed mouse block")
            found = (start, cursor)
            depth = 0
            continue

        depth += _core._brace_delta(line)
        if depth < 0:
            depth = 0

    if found is None:
        return None
    start, end = found
    return lines, start, end


def _parse_mouse(lines: list[str], start: int, end: int, path: Path) -> OrderedDict[str, object]:
    mouse = OrderedDict([
        ("engine", "virtual_stick"),
        ("updateMs", 8),
        ("response", OrderedDict([
            ("pressMs", 45),
            ("releaseMs", 2),
            ("curve", "smoothstep"),
        ])),
        ("speed", OrderedDict([
            ("normal", 1000),
            ("precision", 800),
            ("fine", 240),
            ("fast", 4400),
        ])),
        ("socd", "neutral"),
        ("tapNudgePixels", 1),
        ("maxCatchupMs", 32),
    ])
    section: str | None = None
    seen: set[str] = set()

    for index in range(start + 1, end - 1):
        lineno = index + 1
        line = _core.strip_comment(lines[index]).strip()
        if not line:
            continue

        subsection = re.fullmatch(r"(response|speed)\s*\{", line)
        if subsection:
            if section is not None:
                raise DslError(path, lineno, "mouse blocks may not be nested beyond response/speed")
            section = subsection.group(1)
            continue
        if line == "}":
            if section is None:
                raise DslError(path, lineno, "unexpected '}' inside mouse block")
            section = None
            continue

        match = re.fullmatch(r"([A-Za-z0-9_]+)\s*=\s*(.+)", line)
        if not match:
            raise DslError(path, lineno, f"unknown mouse setting: {line}")
        setting = match.group(1)
        raw_value = match.group(2)
        location = f"{section}.{setting}" if section else setting
        if location in seen:
            raise DslError(path, lineno, f"duplicate mouse setting '{location}'")
        seen.add(location)

        if section == "response":
            response = mouse["response"]
            assert isinstance(response, OrderedDict)
            if setting == "press":
                response["pressMs"] = _duration_ms(path, lineno, raw_value, "response.press", positive=False)
            elif setting == "release":
                response["releaseMs"] = _duration_ms(path, lineno, raw_value, "response.release", positive=False)
            elif setting == "curve":
                value = _token(path, lineno, raw_value, "response.curve")
                if value not in {"linear", "smoothstep"}:
                    raise DslError(path, lineno, "mouse.response.curve supports 'linear' or 'smoothstep'")
                response["curve"] = value
            else:
                raise DslError(path, lineno, f"unknown mouse.response setting '{setting}'")
            continue

        if section == "speed":
            speed = mouse["speed"]
            assert isinstance(speed, OrderedDict)
            if setting not in {"normal", "precision", "fine", "fast"}:
                raise DslError(path, lineno, f"unknown mouse.speed setting '{setting}'")
            speed[setting] = _speed(path, lineno, raw_value, setting)
            continue

        if section is not None:
            raise DslError(path, lineno, f"unknown mouse subsection '{section}'")

        if setting == "engine":
            value = _token(path, lineno, raw_value, "engine")
            if value != "virtual_stick":
                raise DslError(path, lineno, "mouse.engine currently supports only 'virtual_stick'")
            mouse["engine"] = value
        elif setting == "update":
            mouse["updateMs"] = _duration_ms(path, lineno, raw_value, "update", positive=True)
        elif setting == "socd":
            value = _token(path, lineno, raw_value, "socd")
            if value != "neutral":
                raise DslError(path, lineno, "mouse.socd currently supports only 'neutral'")
            mouse["socd"] = value
        elif setting == "tap_nudge":
            mouse["tapNudgePixels"] = _pixels(path, lineno, raw_value, "tap_nudge")
        elif setting == "max_catchup":
            mouse["maxCatchupMs"] = _duration_ms(path, lineno, raw_value, "max_catchup", positive=True)
        else:
            raise DslError(path, lineno, f"unknown mouse setting '{setting}'")

    if section is not None:
        raise DslError(path, end, f"unclosed mouse.{section} block")
    return mouse


def compile_dsl(text: str, path: Path) -> dict[str, object]:
    found = _find_mouse_block(text, path)
    if found is None:
        return _core.compile_dsl(text, path)

    lines, start, end = found
    mouse = _parse_mouse(lines, start, end, path)
    stripped = list(lines)
    for index in range(start, end):
        stripped[index] = ""

    profile = _core.compile_dsl("\n".join(stripped), path)
    profile["mouse"] = mouse
    return profile


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compile iKeyd DSL to the static JSON profile IR.")
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
