from pathlib import Path

runtime = Path("src/iKeyd.App/IKeydRuntimeHandler.cs")
text = runtime.read_text(encoding="utf-8")

physical_marker = '''            case KeyCode.F:\n                if (state.IsExact(LayerKey.M)) { _send.Send("{vkF3sc029}"); return true; }\n                if (state.IsExact(LayerKey.M, LayerKey.H)) { _send.SendKey(WindowsKeyMap.CapsLock); return true; }\n                if (state.IsExact(LayerKey.H, LayerKey.M)) { _send.SendKey(WindowsKeyMap.Insert); return true; }\n                break;'''
if physical_marker not in text:
    anchor = '''            case KeyCode.Y:\n                return DispatchMacroKey('Y', state);'''
    if text.count(anchor) != 1:
        raise SystemExit(f"physical processF insertion anchor count={text.count(anchor)}")
    text = text.replace(
        anchor,
        physical_marker + '''\n\n            case KeyCode.Y:\n                return DispatchMacroKey('Y', state);''',
        1,
    )

macro_marker = '''        if (name == "F")\n        {\n            if (state == "M") { _send.Send("{vkF3sc029}"); return true; }\n            if (state == "MH") { _send.SendKey(WindowsKeyMap.CapsLock); return true; }\n            if (state == "HM") { _send.SendKey(WindowsKeyMap.Insert); return true; }\n        }'''
if macro_marker not in text:
    anchor = '''        if (name is "Y" or "H")\n            return DispatchMacroKey(name[0], state);'''
    if text.count(anchor) != 1:
        raise SystemExit(f"macro processF insertion anchor count={text.count(anchor)}")
    text = text.replace(
        anchor,
        macro_marker + '''\n\n        if (name is "Y" or "H")\n            return DispatchMacroKey(name[0], state);''',
        1,
    )

runtime.write_text(text, encoding="utf-8")

# main briefly carried a repaired SM+H toggle expectation. The pinned source contains
# `if s tate = U`, so the observable compatibility target while the right button is up
# is intentionally no-op; release-only behavior for a pre-held button has a separate test.
scenario = Path("tests/iKeyd.Compatibility.Tests/Scenarios/runtime-mouse-right-hold-toggle-sm-h.json")
if scenario.exists():
    import json
    data = json.loads(scenario.read_text(encoding="utf-8"))
    data["description"] = "Space then M plus H preserves the legacy typo: while right button is up, repeated H is a no-op."
    data.setdefault("expected", {})["actions"] = []
    scenario.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
