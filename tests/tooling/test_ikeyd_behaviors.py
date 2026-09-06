from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd-dsl.py"
spec = importlib.util.spec_from_file_location("ikeyd_dsl_behaviors", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class IKeydBehaviorDslTests(unittest.TestCase):
    def test_fixture_compiles_layer_tap_mod_tap_and_block_behavior(self):
        path = ROOT / "tests" / "tooling" / "fixtures" / "behaviors.ikeyd"
        profile = module.compile_dsl(path.read_text(encoding="utf-8"), path)

        self.assertEqual(
            {"kind": "key", "value": "Left"},
            profile["layers"]["NAV"]["H"],
        )
        self.assertEqual(
            {
                "timeoutMs": 180,
                "interrupt": "hold",
                "tap": {"kind": "key", "value": "Space"},
                "hold": {"kind": "layer", "value": "NAV"},
            },
            profile["behaviors"]["Space"],
        )
        self.assertEqual(
            {"kind": "modifier", "value": "Control"},
            profile["behaviors"]["A"]["hold"],
        )
        self.assertNotIn("tap", profile["behaviors"]["Muhenkan"])
        self.assertEqual(200, profile["behaviors"]["Henkan"]["timeoutMs"])
        self.assertEqual("tap", profile["behaviors"]["Henkan"]["interrupt"])

    def test_pos_reference_uses_selected_jis109_keyboard(self):
        profile = module.compile_dsl(
            """
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layer NAV {
    POS.Ro = key(Escape)
}
behavior POS.Muhenkan = layer(NAV)
keymap S { 
}
""".strip().replace("keymap S { \n}", "keymap S {\n    Q = q\n}\nkeymap K {\n    Q = q\n}"),
            Path("profile.ikeyd"),
        )
        self.assertIn("Ro", profile["layers"]["NAV"])
        self.assertIn("Muhenkan", profile["behaviors"])

    def test_unknown_behavior_layer_is_rejected(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard JIS109
behavior POS.Space = layer_tap(MISSING, Space)
keymap S {
    Q = q
}
keymap K {
    Q = q
}
""".strip()
        with self.assertRaisesRegex(module.DslError, r"references unknown layer 'MISSING'"):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_unknown_modifier_reports_source_line(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard JIS109
behavior POS.A = mod_tap(HyperMega, A)
keymap S {
    Q = q
}
keymap K {
    Q = q
}
""".strip()
        with self.assertRaisesRegex(module.DslError, r"profile\.ikeyd:5: unknown modifier 'HyperMega'"):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_invalid_interrupt_policy_is_rejected(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layer NAV {
    H = key(Left)
}
behavior POS.Space {
    tap = key(Space)
    hold = layer(NAV)
    interrupt = maybe
}
keymap S {
    Q = q
}
keymap K {
    Q = q
}
""".strip()
        with self.assertRaisesRegex(module.DslError, r"interrupt must be 'hold' or 'tap'"):
            module.compile_dsl(text, Path("profile.ikeyd"))


if __name__ == "__main__":
    unittest.main()
