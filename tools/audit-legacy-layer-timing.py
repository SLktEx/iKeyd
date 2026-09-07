#!/usr/bin/env python3
"""Audit whether pinned hotkeySKG layer handlers contain explicit timing logic.

This intentionally reports only handler names, line ranges and timing-control token
names; it does not print decrypted source text.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

HANDLERS = {
    "nonconvert-down": re.compile(r"^\s*vk1Dsc07B::", re.IGNORECASE),
    "nonconvert-up": re.compile(r"^\s*vk1Dsc07B\s+up::", re.IGNORECASE),
    "convert-down": re.compile(r"^\s*vk1Csc079::", re.IGNORECASE),
    "convert-up": re.compile(r"^\s*vk1Csc079\s+up::", re.IGNORECASE),
    "space-down": re.compile(r"^\s*Space::", re.IGNORECASE),
    "space-up": re.compile(r"^\s*Space\s+up::", re.IGNORECASE),
}

TIMING_TOKENS = {
    "Sleep": re.compile(r"\bSleep\b", re.IGNORECASE),
    "KeyWait": re.compile(r"\bKeyWait\b", re.IGNORECASE),
    "SetTimer": re.compile(r"\bSetTimer\b", re.IGNORECASE),
    "A_TickCount": re.compile(r"\bA_TickCount\b", re.IGNORECASE),
    "Critical": re.compile(r"\bCritical\b", re.IGNORECASE),
    "Thread": re.compile(r"\bThread\b", re.IGNORECASE),
}

GLOBAL_THREAD_CONTROLS = {
    "#MaxThreads": re.compile(r"^\s*#MaxThreads\b", re.IGNORECASE),
    "#MaxThreadsPerHotkey": re.compile(r"^\s*#MaxThreadsPerHotkey\b", re.IGNORECASE),
    "#MaxThreadsBuffer": re.compile(r"^\s*#MaxThreadsBuffer\b", re.IGNORECASE),
    "Thread Interrupt": re.compile(r"^\s*Thread\s*,\s*Interrupt\b", re.IGNORECASE),
    "Critical": re.compile(r"^\s*Critical(?:\s|,|$)", re.IGNORECASE),
}


def find_block(lines: list[str], label_re: re.Pattern[str]) -> tuple[int, int]:
    start = next((i for i, line in enumerate(lines) if label_re.search(line)), None)
    if start is None:
        raise ValueError(f"required handler not found: {label_re.pattern}")

    # Legacy hotkey handlers terminate with an explicit Return. Keep the scan
    # bounded to that subroutine so unrelated timers elsewhere do not taint it.
    for i in range(start + 1, len(lines)):
        if re.match(r"^\s*return\s*(?:;.*)?$", lines[i], re.IGNORECASE):
            return start, i
    raise ValueError(f"handler at line {start + 1} has no terminating Return")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    args = parser.parse_args()

    lines = args.source.read_text(encoding="utf-8-sig").splitlines()
    failures: list[str] = []

    print("legacy layer timing audit")
    for name, label_re in HANDLERS.items():
        start, end = find_block(lines, label_re)
        hits: list[str] = []
        block = lines[start : end + 1]
        for token_name, token_re in TIMING_TOKENS.items():
            if any(token_re.search(line) for line in block):
                hits.append(token_name)
        print(f"  {name}: lines {start + 1}-{end + 1}; timing-controls={','.join(hits) if hits else 'none'}")
        if hits:
            failures.append(f"{name} contains explicit timing controls: {', '.join(hits)}")

    global_hits: list[str] = []
    for name, pattern in GLOBAL_THREAD_CONTROLS.items():
        found = [i + 1 for i, line in enumerate(lines) if pattern.search(line)]
        if found:
            global_hits.append(f"{name}@{','.join(map(str, found))}")
    print(f"  global-thread-controls={';'.join(global_hits) if global_hits else 'none'}")

    if failures:
        for failure in failures:
            print(f"ERROR: {failure}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
