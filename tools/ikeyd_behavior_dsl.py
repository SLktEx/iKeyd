from __future__ import annotations

import json
import re
from collections import OrderedDict
from pathlib import Path
from typing import Any


def _strip_comment(line: str) -> str:
    in_string = False
    escaped = False
    for index, ch in enumerate(line):
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
        elif ch == "/" and index + 1 < len(line) and line[index + 1] == "/":
            return line[:index]
    return line


def _compact_key_map(base: Any) -> dict[str, str]:
    keys = [key for row in base.JIS109_LAYOUT for key in row] + ["NumpadComma"]
    aliases = {
        "esc": "Escape",
        "return": "Enter",
        "spc": "Space",
        "bs": "Backspace",
        "bspc": "Backspace",
        "pgup": "PageUp",
        "pgdn": "PageDown",
        "lctrl": "LeftControl",
        "rctrl": "RightControl",
        "lshift": "LeftShift",
        "rshift": "RightShift",
        "lalt": "LeftAlt",
        "ralt": "RightAlt",
        "lgui": "LeftGui",
        "rgui": "RightGui",
        "lwin": "LeftGui",
        "rwin": "RightGui",
        "semicolon": "SColon",
        "period": "Dot",
    }
    result = {key.casefold(): key for key in keys}
    result.update(aliases)
    return result


def _canonical_key(base: Any, path: Path, lineno: int, value: str) -> str:
    mapping = _compact_key_map(base)
    canonical = mapping.get(value.casefold())
    if canonical is None:
        raise base.DslError(path, lineno, f"unknown behavior key '{value}'")
    return canonical


def _scan_layouts(base: Any, lines: list[str], path: Path) -> OrderedDict[str, list[list[str]]]:
    layouts: OrderedDict[str, list[list[str]]] = OrderedDict()
    current: str | None = None
    for lineno, raw in enumerate(lines, 1):
        line = _strip_comment(raw).strip()
        if not line:
            continue
        if current is not None:
            if line == "}":
                current = None
                continue
            row = re.fullmatch(r"row\s+(.+)", line)
            if row:
                layouts[current].append(base.parse_layout_row(path, lineno, row.group(1)))
            continue
        keyboard = re.fullmatch(rf"keyboard\s+({base.IDENT})\s*;?", line)
        if keyboard and keyboard.group(1).casefold() == "jis109":
            layouts["JIS109"] = [row.copy() for row in base.JIS109_LAYOUT]
            continue
        layout = re.fullmatch(rf"layout\s+({base.IDENT})\s*\{{", line)
        if layout:
            current = layout.group(1)
            layouts.setdefault(current, [])
    return layouts


def _action(base: Any, path: Path, lineno: int, expression: str, *, allow_hold: bool) -> OrderedDict[str, str]:
    expression = expression.strip().rstrip(";").strip()
    call = re.fullmatch(rf"({base.IDENT})\((.*)\)", expression)
    if not call:
        raise base.DslError(path, lineno, f"invalid behavior action '{expression}'")
    kind = call.group(1).casefold()
    arg = call.group(2).strip()
    if kind == "key":
        if not re.fullmatch(base.IDENT, arg):
            raise base.DslError(path, lineno, "key(...) expects one key name")
        return OrderedDict(kind="key", value=_canonical_key(base, path, lineno, arg))
    if kind == "text":
        try:
            value = json.loads(arg)
        except json.JSONDecodeError as exc:
            raise base.DslError(path, lineno, f"text(...) requires a quoted string: {exc.msg}") from exc
        if not isinstance(value, str):
            raise base.DslError(path, lineno, "text(...) requires a quoted string")
        return OrderedDict(kind="text", value=value)
    if kind == "layer" and allow_hold:
        if not re.fullmatch(base.IDENT, arg):
            raise base.DslError(path, lineno, "layer(...) expects one layer name")
        return OrderedDict(kind="layer", value=arg)
    if kind == "modifier" and allow_hold:
        modifiers = {
            "ctrl": "Control", "control": "Control", "shift": "Shift",
            "alt": "Alt", "gui": "Gui", "win": "Gui", "super": "Gui",
        }
        value = modifiers.get(arg.casefold())
        if value is None:
            raise base.DslError(path, lineno, f"unknown modifier '{arg}'")
        return OrderedDict(kind="modifier", value=value)
    allowed = "key(...), text(...), layer(...) or modifier(...)" if allow_hold else "key(...) or text(...)"
    raise base.DslError(path, lineno, f"action must be {allowed}")


def _new_behavior() -> OrderedDict[str, object]:
    return OrderedDict(timeoutMs=180, interrupt="hold")


def _shorthand(base: Any, path: Path, lineno: int, expression: str) -> OrderedDict[str, object]:
    expression = expression.strip().rstrip(";").strip()
    layer_tap = re.fullmatch(rf"layer_tap\(\s*({base.IDENT})\s*,\s*({base.IDENT})\s*\)", expression, re.I)
    if layer_tap:
        result = _new_behavior()
        result["tap"] = OrderedDict(kind="key", value=_canonical_key(base, path, lineno, layer_tap.group(2)))
        result["hold"] = OrderedDict(kind="layer", value=layer_tap.group(1))
        return result
    mod_tap = re.fullmatch(rf"mod_tap\(\s*({base.IDENT})\s*,\s*({base.IDENT})\s*\)", expression, re.I)
    if mod_tap:
        result = _new_behavior()
        result["tap"] = OrderedDict(kind="key", value=_canonical_key(base, path, lineno, mod_tap.group(2)))
        result["hold"] = _action(base, path, lineno, f"modifier({mod_tap.group(1)})", allow_hold=True)
        return result
    hold = _action(base, path, lineno, expression, allow_hold=True)
    if hold["kind"] not in {"layer", "modifier"}:
        raise base.DslError(path, lineno, "behavior shorthand must be layer_tap, mod_tap, layer(...) or modifier(...)")
    result = _new_behavior()
    result["hold"] = hold
    return result


def _validate_behavior(base: Any, path: Path, lineno: int, trigger: str, behavior: OrderedDict[str, object], layers: OrderedDict[str, object], *, check_layer: bool = True) -> None:
    hold = behavior.get("hold")
    if not isinstance(hold, dict) or hold.get("kind") not in {"layer", "modifier"}:
        raise base.DslError(path, lineno, f"behavior '{trigger}' requires hold = layer(...) or modifier(...)")
    tap = behavior.get("tap")
    if isinstance(tap, dict) and tap.get("kind") not in {"key", "text"}:
        raise base.DslError(path, lineno, f"behavior '{trigger}' tap action must be key or text")
    if check_layer and hold.get("kind") == "layer":
        name = str(hold.get("value"))
        if not any(existing.casefold() == name.casefold() for existing in layers):
            raise base.DslError(path, lineno, f"behavior '{trigger}' references unknown layer '{name}'")


def extract(base: Any, text: str, path: Path) -> tuple[str, OrderedDict[str, OrderedDict[str, object]], OrderedDict[str, OrderedDict[str, object]]]:
    lines = text.splitlines()
    layouts = _scan_layouts(base, lines, path)
    clean = lines.copy()
    layers: OrderedDict[str, OrderedDict[str, object]] = OrderedDict()
    behaviors: OrderedDict[str, OrderedDict[str, object]] = OrderedDict()
    extracted: str | None = None
    extracted_name: str | None = None
    behavior_trigger: str | None = None
    behavior_draft: OrderedDict[str, object] | None = None
    ordinary_depth = 0

    for index, raw in enumerate(lines):
        lineno = index + 1
        line = _strip_comment(raw).strip()
        if not line:
            continue

        if extracted is not None:
            clean[index] = ""
            if line == "}":
                if extracted == "behavior":
                    assert behavior_trigger is not None and behavior_draft is not None
                    _validate_behavior(base, path, lineno, behavior_trigger, behavior_draft, layers)
                    behaviors[behavior_trigger] = behavior_draft
                extracted = extracted_name = behavior_trigger = None
                behavior_draft = None
                continue
            if extracted == "layer":
                match = re.fullmatch(rf"({base.KEY_REF})\s*=\s*(.+)", line)
                if not match:
                    raise base.DslError(path, lineno, f"unknown layer statement: {line}")
                key = _canonical_key(base, path, lineno, base.resolve_key_ref(path, lineno, match.group(1), layouts))
                layer = layers[extracted_name or ""]
                if any(existing.casefold() == key.casefold() for existing in layer):
                    raise base.DslError(path, lineno, f"duplicate key '{key}' in behavior layer '{extracted_name}'")
                layer[key] = _action(base, path, lineno, match.group(2), allow_hold=False)
                continue

            assert behavior_draft is not None
            assignment = re.fullmatch(rf"({base.IDENT})\s*=\s*(.+)", line)
            if not assignment:
                raise base.DslError(path, lineno, f"unknown behavior setting: {line}")
            name = assignment.group(1).casefold()
            value = assignment.group(2).strip().rstrip(";").strip()
            if name == "tap":
                if "tap" in behavior_draft:
                    raise base.DslError(path, lineno, "duplicate behavior tap setting")
                behavior_draft["tap"] = _action(base, path, lineno, value, allow_hold=False)
            elif name == "hold":
                if "hold" in behavior_draft:
                    raise base.DslError(path, lineno, "duplicate behavior hold setting")
                hold = _action(base, path, lineno, value, allow_hold=True)
                if hold["kind"] not in {"layer", "modifier"}:
                    raise base.DslError(path, lineno, "hold must be layer(...) or modifier(...)")
                behavior_draft["hold"] = hold
            elif name == "timeout":
                timeout = re.fullmatch(r"(\d+)\s*ms", value, re.I)
                if not timeout or int(timeout.group(1)) <= 0:
                    raise base.DslError(path, lineno, "timeout must be a positive duration such as 180ms")
                behavior_draft["timeoutMs"] = int(timeout.group(1))
            elif name == "interrupt":
                policy = value.casefold()
                if policy not in {"hold", "tap"}:
                    raise base.DslError(path, lineno, "interrupt must be 'hold' or 'tap'")
                behavior_draft["interrupt"] = policy
            else:
                raise base.DslError(path, lineno, f"unknown behavior setting '{assignment.group(1)}'")
            continue

        if ordinary_depth:
            if line.endswith("{"):
                ordinary_depth += 1
            if line == "}":
                ordinary_depth -= 1
            continue

        if re.fullmatch(rf"(?:profile\s+{base.IDENT}|layout\s+{base.IDENT}|keymap\s+{base.IDENT}(?:\s+using\s+{base.IDENT})?|quirks)\s*\{{", line):
            ordinary_depth = 1
            continue

        layer_start = re.fullmatch(rf"layer\s+({base.IDENT})\s*\{{", line)
        if layer_start:
            name = layer_start.group(1)
            if any(existing.casefold() == name.casefold() for existing in layers):
                raise base.DslError(path, lineno, f"duplicate behavior layer '{name}'")
            layers[name] = OrderedDict()
            extracted, extracted_name = "layer", name
            clean[index] = ""
            continue

        block = re.fullmatch(rf"behavior\s+({base.KEY_REF})\s*\{{", line)
        if block:
            trigger = _canonical_key(base, path, lineno, base.resolve_key_ref(path, lineno, block.group(1), layouts))
            if any(existing.casefold() == trigger.casefold() for existing in behaviors):
                raise base.DslError(path, lineno, f"duplicate behavior trigger '{trigger}'")
            extracted, behavior_trigger, behavior_draft = "behavior", trigger, _new_behavior()
            clean[index] = ""
            continue

        short = re.fullmatch(rf"behavior\s+({base.KEY_REF})\s*=\s*(.+)", line)
        if short:
            trigger = _canonical_key(base, path, lineno, base.resolve_key_ref(path, lineno, short.group(1), layouts))
            if any(existing.casefold() == trigger.casefold() for existing in behaviors):
                raise base.DslError(path, lineno, f"duplicate behavior trigger '{trigger}'")
            behavior = _shorthand(base, path, lineno, short.group(2))
            _validate_behavior(base, path, lineno, trigger, behavior, layers, check_layer=False)
            behaviors[trigger] = behavior
            clean[index] = ""

    if extracted is not None:
        raise base.DslError(path, len(lines), f"unclosed {extracted} block")
    for trigger, behavior in behaviors.items():
        _validate_behavior(base, path, 1, trigger, behavior, layers)
    return "\n".join(clean), layers, behaviors


def merge(profile: dict[str, object], layers: OrderedDict[str, object], behaviors: OrderedDict[str, object]) -> OrderedDict[str, object]:
    result: OrderedDict[str, object] = OrderedDict()
    for key, value in profile.items():
        if key == "knownQuirks":
            if layers:
                result["layers"] = layers
            if behaviors:
                result["behaviors"] = behaviors
        result[key] = value
    if "knownQuirks" not in profile:
        if layers:
            result["layers"] = layers
        if behaviors:
            result["behaviors"] = behaviors
    return result
