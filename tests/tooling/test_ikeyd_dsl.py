from __future__ import annotations

import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd-dsl.py"
spec = importlib.util.spec_from_file_location("ikeyd_dsl", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class IKeydDslTests(unittest.TestCase):
    def test_current_hotkeyskg_dsl_compiles_to_canonical_json(self):
        dsl_path = ROOT / "config" / "hotkeySKG.ikeyd"
        json_path = ROOT / "config" / "hotkeySKG.behavior.json"

        actual = module.compile_dsl(dsl_path.read_text(encoding="utf-8"), dsl_path)
        expected = json.loads(json_path.read_text(encoding="utf-8"))

        self.assertEqual(expected, actual)

    def test_duplicate_chords_preserve_legacy_first_declaration_wins_metadata(self):
        dsl_path = ROOT / "config" / "hotkeySKG.ikeyd"
        profile = module.compile_dsl(dsl_path.read_text(encoding="utf-8"), dsl_path)

        self.assertEqual(
            [
                {
                    "keys": ["scolon", "v"],
                    "outputs": ["nya", "pya"],
                    "effectiveOutput": "nya",
                },
                {
                    "keys": ["f", "u"],
                    "outputs": ["she", "je"],
                    "effectiveOutput": "she",
                },
            ],
            profile["knownQuirks"]["duplicateChordPatterns"]["K"],
        )

    def test_position_references_compile_to_physical_key_ids(self):
        text = """
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W E
    row A S D
}
keymap BASE {
    BASE[1,1] = "x"
    BASE[1,2] = "y"
    combo BASE[1,1] + BASE[2,2] = "escape"
    combo POS[1,2] + E = "other"
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))

        self.assertEqual({"Q": "x", "W": "y"}, profile["singleStroke"]["BASE"])
        self.assertEqual(
            [["Q", "S", "escape"], ["W", "E", "other"]],
            profile["chords"]["BASE"],
        )
        self.assertNotIn("layouts", profile)

    def test_position_references_are_independent_of_base_outputs(self):
        first = """
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W
}
keymap BASE {
    Q = "q"
    W = "w"
    combo BASE[1,1] + BASE[1,2] = "escape"
}
""".strip()
        second = first.replace('Q = "q"', 'Q = "x"').replace('W = "w"', 'W = "y"')

        first_profile = module.compile_dsl(first, Path("first.ikeyd"))
        second_profile = module.compile_dsl(second, Path("second.ikeyd"))

        self.assertEqual(
            first_profile["chords"]["BASE"],
            second_profile["chords"]["BASE"],
        )
        self.assertEqual([["Q", "W", "escape"]], second_profile["chords"]["BASE"])

    def test_behavior_invocation_compiles_separately_from_string_mappings(self):
        text = """
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W E
}
keymap BASE {
    POS[1,1] = LT(NUM, Z)
    POS[1,2] = "w"
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))

        self.assertEqual({"W": "w"}, profile["singleStroke"]["BASE"])
        self.assertEqual(
            {
                "Q": {
                    "name": "LT",
                    "arguments": ["NUM", "Z"],
                }
            },
            profile["behaviors"]["BASE"],
        )

    def test_behavior_option_block_compiles_as_generic_invocation_options(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap S {
    A = LT(NUM, Z) {
        tapping_term = 170ms
        hold_on_other_key_press = false
    }
    B = MT(Ctrl, X)
}
keymap K {
}
keymap NUM {
    C = "num-c"
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))

        self.assertEqual(
            {
                "name": "LT",
                "arguments": ["NUM", "Z"],
                "options": {
                    "tapping_term": "170ms",
                    "hold_on_other_key_press": "false",
                },
            },
            profile["behaviors"]["S"]["A"],
        )
        self.assertEqual(
            {"name": "MT", "arguments": ["Ctrl", "X"]},
            profile["behaviors"]["S"]["B"],
        )

    def test_duplicate_behavior_option_is_rejected(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap S {
    A = LT(NUM, Z) {
        tapping_term = 170ms
        tapping_term = 180ms
    }
}
""".strip()

        with self.assertRaisesRegex(module.DslError, r"duplicate behavior option 'tapping_term'"):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_behavior_invocation_rejects_non_identifier_arguments_for_now(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap BASE {
    Q = LT(NUM, bad-arg)
}
""".strip()

        with self.assertRaisesRegex(
            module.DslError,
            r"profile\.ikeyd:5: behavior arguments must be identifiers",
        ):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_string_and_behavior_mapping_cannot_share_the_same_key(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap BASE {
    Q = "q"
    Q = LT(NUM, Z)
}
""".strip()

        with self.assertRaisesRegex(module.DslError, r"duplicate key mapping 'BASE\.Q'"):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_position_reference_reports_out_of_range_coordinates(self):
        text = """
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W
}
keymap BASE {
    combo BASE[1,3] + BASE[1,1] = "escape"
}
""".strip()

        with self.assertRaisesRegex(
            module.DslError,
            r"profile\.ikeyd:8: column 3 is out of range for layout 'BASE' row 1",
        ):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_reports_source_line_for_invalid_statement(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap S {
    this is not valid
}
""".strip()

        with self.assertRaisesRegex(module.DslError, r"profile\.ikeyd:5: unknown keymap statement"):
            module.compile_dsl(text, Path("profile.ikeyd"))


if __name__ == "__main__":
    unittest.main()
