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

    def test_layout_map_expands_rows_to_physical_keys(self):
        text = '''
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
'''.strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual(
            {"Q": "x", "W": "y", "E": "z", "A": "a", "S": "b", "D": "c"},
            profile["singleStroke"]["S"],
        )

    def test_grouped_combos_preserve_order_and_duplicates(self):
        text = '''
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
'''.strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual(
            [["F", "U", "first"], ["F", "H", "other"], ["F", "U", "second"]],
            profile["chords"]["K"],
        )
        self.assertEqual(
            [{"keys": ["f", "u"], "outputs": ["first", "second"], "effectiveOutput": "first"}],
            profile["knownQuirks"]["duplicateChordPatterns"]["K"],
        )

    def test_position_references_resolve_through_base_layout(self):
        text = '''
profile demo {
    chord_window = 40ms
}
layout BASE {
    row Q W
}
keymap S {
    BASE[1,1] = "q"
    combo POS[1,1] + BASE[1,2] = "qw"
}
'''.strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual({"Q": "q"}, profile["singleStroke"]["S"])
        self.assertEqual([["Q", "W", "qw"]], profile["chords"]["S"])

    def test_map_width_mismatch_reports_source_line(self):
        text = '''
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
'''.strip()

        with self.assertRaisesRegex(module.DslError, r"profile\.ikeyd:9: map row 1 has 2 outputs"):
            module.compile_dsl(text, Path("profile.ikeyd"))

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

    def test_reports_source_line_for_invalid_statement(self):
        text = '''
profile demo {
    chord_window = 40ms
}
keymap S {
    this is not valid
}
'''.strip()

        with self.assertRaisesRegex(module.DslError, r"profile\.ikeyd:5: unknown keymap statement"):
            module.compile_dsl(text, Path("profile.ikeyd"))


if __name__ == "__main__":
    unittest.main()
