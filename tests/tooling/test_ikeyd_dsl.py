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
