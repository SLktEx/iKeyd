#!/usr/bin/env python3
"""Compile the iKeyd authoring DSL, including configurable key behaviors."""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path


def _load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"could not load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


_HERE = Path(__file__).resolve().parent
_base = _load("ikeyd_dsl_base", _HERE / "ikeyd_dsl_base.py")
_behavior = _load("ikeyd_behavior_dsl", _HERE / "ikeyd_behavior_dsl.py")

# Preserve the public surface used by the existing tooling tests. The base parser
# stays byte-for-byte identical to the pre-behavior compiler; this wrapper only
# strips/merges the new orthogonal behavior extension.
for _name in dir(_base):
    if not _name.startswith("__"):
        globals().setdefault(_name, getattr(_base, _name))

_base_compile_dsl = _base.compile_dsl


def compile_dsl(text: str, path: Path) -> dict[str, object]:
    clean, layers, behaviors = _behavior.extract(_base, text, path)
    profile = _base_compile_dsl(clean, path)
    return _behavior.merge(profile, layers, behaviors)


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
