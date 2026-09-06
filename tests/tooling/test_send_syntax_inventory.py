from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "analyze-send-syntax.py"
spec = importlib.util.spec_from_file_location("send_syntax_inventory", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
spec.loader.exec_module(module)


class SendSyntaxInventoryTests(unittest.TestCase):
    def test_classifies_real_send_syntax_families_and_rejects_sendmessage_false_positive(self):
        matrix = {
            "source": {"sha256": "abc"},
            "features": [
                self.feature("plain", "Send,hello", "hello"),
                self.feature("named", "Send,{Enter}", "{Enter}"),
                self.feature("modifier", "Send,^!{Tab}", "^!{Tab}"),
                self.feature("repeat", "Send,{Left 3}", "{Left 3}"),
                self.feature("state", "Send,{Shift down}", "{Shift down}"),
                self.feature("dynamic", "Send,%key%", "%key%"),
                {
                    "id": "false-positive",
                    "kind": "send",
                    "line": 7,
                    "owner": "function:IME_IfRomaKana",
                    "text": 'imeget := DllCall("SendMessage"',
                    "details": {"sendCommand": "Send", "expression": 'Message"'},
                },
            ],
        }

        report = module.build_inventory(matrix)

        self.assertEqual(7, report["summary"]["scannerSendFeatureCount"])
        self.assertEqual(6, report["summary"]["actualSendFeatureCount"])
        self.assertEqual(1, report["summary"]["scannerFalsePositiveCount"])
        self.assertEqual(6, report["summary"]["uniqueExpressionCount"])
        self.assertEqual(["false-positive"], [item["id"] for item in report["scannerFalsePositives"]])

        by_expression = {item["expression"]: item for item in report["expressions"]}
        self.assertIn("plain-text", by_expression["hello"]["families"])
        self.assertIn("brace-token", by_expression["{Enter}"]["families"])
        self.assertEqual("^!", by_expression["^!{Tab}"]["modifierPrefix"])
        self.assertIn("repeat-token", by_expression["{Left 3}"]["families"])
        self.assertEqual("repeat", by_expression["{Left 3}"]["braceTokens"][0]["family"])
        self.assertIn("key-state-token", by_expression["{Shift down}"]["families"])
        self.assertTrue(by_expression["%key%"]["dynamic"])

    def test_duplicate_expressions_are_grouped_but_keep_inventory_traceability(self):
        matrix = {
            "features": [
                self.feature("a", "Send,{Enter}", "{Enter}"),
                self.feature("b", "Send,{Enter}", "{Enter}"),
            ]
        }

        report = module.build_inventory(matrix)

        self.assertEqual(2, report["summary"]["actualSendFeatureCount"])
        self.assertEqual(1, report["summary"]["uniqueExpressionCount"])
        self.assertEqual(["a", "b"], report["expressions"][0]["inventoryIds"])

    def test_markdown_surfaces_false_positives_and_exact_inventory_ids(self):
        report = module.build_inventory({
            "source": {"sha256": "abc"},
            "features": [self.feature("enter", "Send,{Enter}", "{Enter}")],
        })

        rendered = module.render_markdown(report)

        self.assertIn("# hotkeySKG Send syntax inventory", rendered)
        self.assertIn("`enter`", rendered)
        self.assertIn("Scanner false positives", rendered)

    @staticmethod
    def feature(feature_id: str, text: str, expression: str) -> dict:
        return {
            "id": feature_id,
            "kind": "send",
            "line": 1,
            "owner": "function:test",
            "windowContext": None,
            "text": text,
            "details": {"sendCommand": "Send", "expression": expression},
        }


if __name__ == "__main__":
    unittest.main()
