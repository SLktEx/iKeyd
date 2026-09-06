from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd-dsl.py"
spec = importlib.util.spec_from_file_location("ikeyd_dsl_conditionals", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class IKeydConditionalDslTests(unittest.TestCase):
    def test_when_supports_strings_booleans_optional_else_and_nested_actions(self):
        path = ROOT / "tests" / "tooling" / "fixtures" / "conditional-actions.ikeyd"
        profile = module.compile_dsl(path.read_text(encoding="utf-8"), path)

        q = profile["layers"]["APP"]["Q"]
        self.assertEqual("when", q["kind"])
        self.assertEqual(
            {"query": "foreground.process", "operator": "equals", "value": "Code.exe"},
            q["condition"],
        )
        self.assertEqual({"kind": "key", "value": "Escape"}, q["then"])
        self.assertEqual({"kind": "key", "value": "F1"}, q["else"])

        w = profile["layers"]["APP"]["W"]
        self.assertEqual("not_equals", w["condition"]["operator"])
        self.assertEqual("false", w["condition"]["value"])
        self.assertEqual("a,b", w["then"]["value"])

        e = profile["layers"]["APP"]["E"]
        self.assertEqual("true", e["condition"]["value"])
        self.assertEqual(
            {"kind": "exec", "value": "tool.exe", "args": ["--caps"]},
            e["then"],
        )

        r = profile["layers"]["APP"]["R"]
        self.assertEqual("when", r["then"]["kind"])
        self.assertEqual("keyboard.numlock", r["then"]["condition"]["query"])
        self.assertEqual({"kind": "key", "value": "F3"}, r["then"]["else"])

        t = profile["layers"]["APP"]["T"]
        self.assertNotIn("else", t)

    def test_unknown_query_in_when_is_rejected(self):
        text = '''
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layer APP {
    POS.Q = when(system.magic == "x", key(Escape), key(F1))
}
keymap S {
    Q = q
}
keymap K {
    Q = q
}
'''.strip()

        with self.assertRaisesRegex(module.DslError, r"unknown system query 'system\.magic'"):
            module.compile_dsl(text, Path("conditional.ikeyd"))

    def test_unbalanced_nested_when_is_rejected(self):
        text = '''
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layer APP {
    POS.Q = when(foreground.process == "Code.exe", when(keyboard.capslock == true, key(Escape), key(F1)), key(F2)
}
keymap S {
    Q = q
}
keymap K {
    Q = q
}
'''.strip()

        with self.assertRaises(module.DslError):
            module.compile_dsl(text, Path("conditional.ikeyd"))


if __name__ == "__main__":
    unittest.main()
