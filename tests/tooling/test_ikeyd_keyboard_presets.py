from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd-dsl.py"
spec = importlib.util.spec_from_file_location("ikeyd_dsl_keyboard_presets", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class IKeydKeyboardPresetTests(unittest.TestCase):
    def test_jis109_preset_has_exactly_109_unique_physical_keys(self):
        keys = [key for row in module.JIS109_LAYOUT for key in row]
        self.assertEqual(109, len(keys))
        self.assertEqual(109, len({key.casefold() for key in keys}))
        self.assertIn("Ro", keys)
        self.assertIn("Yen", keys)
        self.assertIn("Henkan", keys)
        self.assertIn("Muhenkan", keys)
        self.assertIn("ZenkakuHankaku", keys)
        self.assertNotIn("NumpadComma", keys)

    def test_keyboard_jis109_registers_named_physical_references(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard JIS109
keymap S {
    JIS109.Ro = ro
    combo JIS109.Muhenkan + JIS109.Ro = escape
    combo JIS109.Henkan + JIS109.Yen = other
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual({"Ro": "ro"}, profile["singleStroke"]["S"])
        self.assertEqual(
            [["Muhenkan", "Ro", "escape"], ["Henkan", "Yen", "other"]],
            profile["chords"]["S"],
        )

    def test_pos_aliases_declared_jis109_physical_keyboard_before_base(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layout BASE {
    row Q W E
}
keymap S {
    POS.Ro = ro
    combo POS.Muhenkan + POS.Yen = physical
    combo BASE[1,1] + BASE[1,2] = logical-subset
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual({"Ro": "ro"}, profile["singleStroke"]["S"])
        self.assertEqual(
            [["Muhenkan", "Yen", "physical"], ["Q", "W", "logical-subset"]],
            profile["chords"]["S"],
        )

    def test_pos_keeps_legacy_base_alias_without_keyboard_preset(self):
        text = """
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W E
}
keymap S {
    POS.Q = q
    combo POS[1,1] + POS.W = legacy
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual({"Q": "q"}, profile["singleStroke"]["S"])
        self.assertEqual([["Q", "W", "legacy"]], profile["chords"]["S"])

    def test_explicit_pos_layout_overrides_keyboard_preset(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layout POS {
    row CustomA CustomB
}
keymap S {
    POS.CustomA = a
    combo POS.CustomA + POS.CustomB = custom
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual({"CustomA": "a"}, profile["singleStroke"]["S"])
        self.assertEqual([["CustomA", "CustomB", "custom"]], profile["chords"]["S"])

    def test_keyboard_preset_names_are_case_insensitive(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard jis109
keymap S {
    JIS109.RO = ro
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual({"Ro": "ro"}, profile["singleStroke"]["S"])

    def test_unknown_keyboard_preset_reports_source_line(self):
        text = """
profile demo {
    chord_window = 40ms
}
keyboard ANSI999
keymap S {
    Q = q
}
""".strip()

        with self.assertRaisesRegex(
            module.DslError,
            r"profile\.ikeyd:4: unknown keyboard preset 'ANSI999'",
        ):
            module.compile_dsl(text, Path("profile.ikeyd"))


if __name__ == "__main__":
    unittest.main()
