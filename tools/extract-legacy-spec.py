#!/usr/bin/env python3
"""Extract the keyboard behavior snapshot used by iKeyd regression tests.

Usage:
    python tools/extract-legacy-spec.py path/to/hotkeySKG.ahk output.json

Chord declaration order is preserved because the legacy AHK runtime scans the
chord table from the beginning and returns the first match.
"""
from __future__ import annotations

import json
import re
import sys
from collections import defaultdict
from pathlib import Path


def extract(source: Path) -> dict:
    lines = source.read_text(encoding="utf-8-sig").splitlines()
    single_declarations = {"S": [], "K": []}
    chords = {"S": [], "K": []}
    flag_defs = []

    for line_no, line in enumerate(lines, 1):
        if match := re.fullmatch(r"flag_([A-Za-z0-9]+):=(.+)", line.strip()):
            flag_defs.append([match.group(1), match.group(2)])

        if match := re.fullmatch(r"singleStroke([SK])_([A-Za-z0-9]+)=(.*)", line):
            mode, key, output = match.groups()
            single_declarations[mode].append([key, output])

        if match := re.fullmatch(r"kCmb([SK])(\d+):=flag_([A-Za-z0-9]+)\|flag_([A-Za-z0-9]+)", line, re.I):
            mode, ordinal, first, second = match.groups()
            mode = mode.upper()
            result_line = lines[line_no] if line_no < len(lines) else ""
            result = re.fullmatch(rf"resultOfKCmb{mode}{ordinal}=(.*)", result_line, re.I)
            chords[mode].append([first, second, result.group(1) if result else None])

    # AHK variable assignment is last-write-wins.
    single = {}
    for mode, declarations in single_declarations.items():
        effective = {}
        original_case = {}
        for key, output in declarations:
            effective[key.lower()] = output
            original_case[key.lower()] = key
        single[mode] = {original_case[key]: output for key, output in effective.items()}

    duplicate_chords = {}
    for mode, declarations in chords.items():
        groups = defaultdict(list)
        for first, second, output in declarations:
            canonical = tuple(sorted((first.lower(), second.lower())))
            groups[canonical].append([first, second, output])
        duplicate_chords[mode] = [
            {
                "keys": list(canonical),
                "outputs": [declaration[2] for declaration in declarations],
                "effectiveOutput": declarations[0][2],
            }
            for canonical, declarations in groups.items()
            if len(declarations) > 1
        ]

    flag_groups = defaultdict(list)
    for key, expression in flag_defs:
        flag_groups[key.lower()].append([key, expression])

    return {
        "source": {
            "runtime": "AutoHotkey v1.1.16.05",
            "executableLines": sum(1 for line in lines if line.strip() and not line.lstrip().startswith(";")),
            "chordWindowMs": 40,
        },
        "singleStroke": single,
        "chords": chords,
        "knownQuirks": {
            "duplicateChordPatterns": duplicate_chords,
            "duplicateFlagDefinitions": [
                {"key": declarations[0][0], "expressions": [declaration[1] for declaration in declarations]}
                for declarations in flag_groups.values()
                if len(declarations) > 1
            ],
        },
    }


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit("usage: extract-legacy-spec.py SOURCE.ahk OUTPUT.json")
    source, output = map(Path, sys.argv[1:])
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(extract(source), ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
