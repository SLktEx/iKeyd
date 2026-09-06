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

    def test_layout_map_expands_rows_to_physical_keys(self):
        text = """
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W E
    row A S D
}
keymap S using BASE {
    map {
        row "x", "y", "z"
        row "a", "b", "c"
    }
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual(
            {"Q": "x", "W": "y", "E": "z", "A": "a", "S": "b", "D": "c"},
            profile["singleStroke"]["S"],
        )

    def test_grouped_combos_preserve_order_and_duplicates(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap K {
    combos F {
        U = "first"
        H = "other"
        U = "second"
    }
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual(
            [["F", "U", "first"], ["F", "H", "other"], ["F", "U", "second"]],
            profile["chords"]["K"],
        )
        self.assertEqual(
            [{"keys": ["f", "u"], "outputs": ["first", "second"], "effectiveOutput": "first"}],
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

        self.assertEqual(first_profile["chords"]["BASE"], second_profile["chords"]["BASE"])
        self.assertEqual([["Q", "W", "escape"]], second_profile["chords"]["BASE"])

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

    def test_map_width_mismatch_reports_source_line(self):
        text = """
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W E
}
keymap S using BASE {
    map {
        row "x", "y"
    }
}
""".strip()

        with self.assertRaisesRegex(module.DslError, r"profile\.ikeyd:9: map row 1 has 2 outputs"):
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
