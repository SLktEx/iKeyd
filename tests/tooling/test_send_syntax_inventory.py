from __future__ import annotations

import importlib.util
import tempfile
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
                self.feature("vk-sc", "Send,{vk1Csc079}", "{vk1Csc079}"),
                self.feature("media", "Send,{VOLUME_UP}", "{VOLUME_UP}"),
                self.feature("click", "Send,^{Click,WU}", "^{Click,WU}"),
                {
                    "id": "false-positive",
                    "kind": "send",
                    "line": 10,
                    "owner": "function:IME_IfRomaKana",
                    "text": 'imeget := DllCall("SendMessage"',
                    "details": {"sendCommand": "Send", "expression": 'Message"'},
                },
            ],
        }

        report = module.build_inventory(matrix)

        self.assertEqual(10, report["summary"]["scannerSendFeatureCount"])
        self.assertEqual(9, report["summary"]["actualSendFeatureCount"])
        self.assertEqual(1, report["summary"]["scannerFalsePositiveCount"])
        self.assertEqual(9, report["summary"]["uniqueExpressionCount"])
        self.assertEqual(["false-positive"], [item["id"] for item in report["scannerFalsePositives"]])

        by_expression = {item["expression"]: item for item in report["expressions"]}
        self.assertIn("plain-text", by_expression["hello"]["families"])
        self.assertIn("brace-token", by_expression["{Enter}"]["families"])
        self.assertEqual("^!", by_expression["^!{Tab}"]["modifierPrefix"])
        self.assertIn("repeat-token", by_expression["{Left 3}"]["families"])
        self.assertEqual("repeat", by_expression["{Left 3}"]["braceTokens"][0]["family"])
        self.assertIn("key-state-token", by_expression["{Shift down}"]["families"])
        self.assertTrue(by_expression["%key%"]["dynamic"])
        self.assertIn("virtual-scan-code-token", by_expression["{vk1Csc079}"]["families"])
        self.assertEqual("virtual-scan-code", by_expression["{vk1Csc079}"]["braceTokens"][0]["family"])
        self.assertIn("media-token", by_expression["{VOLUME_UP}"]["families"])
        self.assertIn("click-token", by_expression["^{Click,WU}"]["families"])

    def test_ahk_inline_source_comments_are_not_counted_as_send_text(self):
        report = module.build_inventory({
            "features": [
                self.feature(
                    "menu",
                    "Send,!{Space}ep     ; command prompt paste",
                    "!{Space}ep     ; command prompt paste",
                )
            ]
        })

        self.assertEqual(1, report["summary"]["inlineCommentNormalizedCount"])
        self.assertEqual("!{Space}ep", report["expressions"][0]["expression"])
        self.assertEqual(
            ["!{Space}ep     ; command prompt paste"],
            report["expressions"][0]["rawExpressions"],
        )

    def test_bounded_dynamic_variables_expand_from_the_pinned_source_structure(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "hotkeySKG.ahk"
            source.write_text(
                "\n".join([
                    "defaultKey_Q=q",
                    "singleStrokeS_Q=ni",
                    "resultOfKCmbS1=fa",
                    "SHKey_Q=#1",
                    "func_J(){",
                    'withFuncKey("{LEFT}","+{LEFT}","^{LEFT}","/* */{LEFT 3}")',
                    "}",
                ]),
                encoding="utf-8",
            )
            matrix = {
                "features": [
                    self.feature("key", "Send,%key%", "%key%"),
                    self.feature("mkey", "Send,^%mkey%", "^%mkey%"),
                    self.feature("mskey", "Send,%mskey%", "%mskey%"),
                    self.feature("string", "Send,%string%", "%string%"),
                    self.feature("macro", "Send,%tempstr%", "%tempstr%"),
                ]
            }

            report = module.build_inventory(matrix, source)
            reach = report["dynamicReachability"]
            assert reach is not None

            self.assertEqual(5, reach["summary"]["dynamicExpressionCount"])
            self.assertEqual(4, reach["summary"]["boundedDynamicExpressionCount"])
            self.assertEqual(1, reach["summary"]["unboundedDynamicExpressionCount"])
            self.assertEqual("user-authored macro remainder", reach["variables"]["tempstr"]["source"])
            self.assertIn("/* */{LEFT 3}", reach["variables"]["mskey"]["values"])

            by_expression = {item["expression"]: item for item in reach["expressions"]}
            self.assertEqual(["q"], [item["expression"] for item in by_expression["%key%"]["reachableExpressions"]])
            self.assertIn("^{LEFT}", [item["expression"] for item in by_expression["^%mkey%"]["reachableExpressions"]])
            self.assertIn("repeat-token", next(
                item for item in by_expression["%mskey%"]["reachableExpressions"]
                if item["expression"] == "/* */{LEFT 3}"
            )["families"])
            self.assertFalse(by_expression["%tempstr%"]["bounded"])

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
