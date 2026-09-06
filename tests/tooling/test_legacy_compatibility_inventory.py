from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "analyze-legacy-compatibility.py"
spec = importlib.util.spec_from_file_location("legacy_inventory", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


SAMPLE = r'''
; comment
SMODE := 1
gmode := SMODE
macroH := ""
singleStrokeS_Q=-
singleStrokeK_Q := o
flag_Q:=1<<1
flag_W:=1<<2
kCmbS1:=flag_Q|flag_W
resultOfKCmbS1=foo
kCmbK1=flag_Q|flag_W
resultOfKCmbK1 = bar
IME_IfRomaKana(){
  imeget := DllCall("SendMessage")
}
#IfWinActive ahk_class ConsoleWindowClass
^v::Send,!{Space}ep
#IfWinActive
^Esc::Suspend,Toggle
q::onKeyDown("_Q")
q up::onKeyUp("_Q")
onKeyDown(keyName){
  if(fstate=="MH"){
    Send,{TAB}
  }
  if(gmode == SMODE)
    WinMinimize,A
  clipboard := gpaste
  ClipWait,1
  MouseMove, 1, 2,,R
  Send,{VOLUME_UP}
}
processMacro(chr){
  InputBox,ipt,Macro%chr%
}
OnClipboardChange:
clipboard := clip00
'''.strip()


class InventoryTests(unittest.TestCase):
    def scan(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        source = Path(tmp.name) / "hotkeySKG.ahk"
        source.write_text(SAMPLE, encoding="utf-8")
        return source, module.scan_source(source)

    def test_extracts_compatibility_surfaces(self):
        _, features = self.scan()
        kinds = {feature.kind for feature in features}
        self.assertIn("single-stroke", kinds)
        self.assertIn("chord", kinds)
        self.assertIn("function", kinds)
        self.assertIn("context", kinds)
        self.assertIn("hotkey", kinds)
        self.assertIn("branch", kinds)
        self.assertIn("send", kinds)
        self.assertIn("window-operation", kinds)
        self.assertIn("clipboard-operation", kinds)
        self.assertIn("macro-operation", kinds)
        self.assertIn("mouse-operation", kinds)
        self.assertIn("ui-operation", kinds)
        self.assertIn("label", kinds)
        self.assertIn("lifecycle-operation", kinds)

    def test_control_flow_is_not_misclassified_as_function(self):
        _, features = self.scan()
        function_names = [f.details["name"].lower() for f in features if f.kind == "function"]
        self.assertIn("onkeydown", function_names)
        self.assertNotIn("if", function_names)
        branch = next(f for f in features if f.kind == "branch" and "fstate" in f.text)
        self.assertEqual("function:onKeyDown", branch.owner)

    def test_tracks_window_context_and_key_up(self):
        _, features = self.scan()
        console = next(f for f in features if f.kind == "hotkey" and f.details["trigger"] == "^v")
        self.assertEqual("#IfWinActive ahk_class ConsoleWindowClass", console.window_context)
        self.assertIn("process-specific", console.tags)
        key_up = next(f for f in features if f.kind == "hotkey" and f.details["trigger"].lower().endswith(" up"))
        self.assertIn("input-state", key_up.tags)

    def test_chords_accept_expression_and_legacy_assignment_forms(self):
        _, features = self.scan()
        chords = [f for f in features if f.kind == "chord"]
        self.assertEqual(2, len(chords))
        by_mode = {f.details["mode"]: f for f in chords}
        self.assertEqual(["Q", "W"], by_mode["S"].details["keys"])
        self.assertEqual("foo", by_mode["S"].details["output"])
        self.assertEqual("bar", by_mode["K"].details["output"])

    def test_single_strokes_accept_whitespace_and_both_assignments(self):
        _, features = self.scan()
        singles = [f for f in features if f.kind == "single-stroke"]
        self.assertEqual({"S", "K"}, {f.details["mode"] for f in singles})

    def test_ids_are_stable_for_same_source(self):
        source, features = self.scan()
        second = module.scan_source(source)
        self.assertEqual([f.feature_id for f in features], [f.feature_id for f in second])

    def test_coverage_rules_are_conservative_and_classified(self):
        _, features = self.scan()
        rules = {
            "defaults": {field: "unknown" for field in module.STATUS_FIELDS},
            "rules": [
                {
                    "match": {"kind": ["single-stroke", "chord"]},
                    "set": {
                        "implementation": "implemented",
                        "unit": "covered",
                        "scenario": "partial",
                        "exeDiff": "partial",
                        "ahkDiff": "partial",
                        "realWindows": "unverified",
                        "intentionalDifference": "no",
                    },
                    "evidence": ["fixture"],
                }
            ],
        }
        module.apply_coverage(features, rules)
        single = next(f for f in features if f.kind == "single-stroke")
        self.assertEqual("implemented", single.coverage["implementation"])
        self.assertEqual("partially-verified", single.classification)
        self.assertEqual(["fixture"], single.evidence)
        function = next(f for f in features if f.kind == "function")
        self.assertEqual("unknown", function.classification)

    def test_repo_rules_reconcile_implemented_long_tail_without_hiding_process_specific_work(self):
        _, features = self.scan()
        rules = json.loads((ROOT / "tests" / "compatibility" / "coverage-rules.json").read_text(encoding="utf-8"))
        module.apply_coverage(features, rules)

        macro = next(f for f in features if f.kind == "macro-operation")
        clipboard = next(f for f in features if f.kind == "clipboard-operation")
        macro_function = next(f for f in features if f.kind == "function" and f.details.get("name") == "processMacro")
        macro_ui = next(f for f in features if f.kind == "ui-operation")
        window = next(f for f in features if f.kind == "window-operation")
        clipboard_label = next(f for f in features if f.kind == "label" and f.text.lower().startswith("onclipboardchange"))
        global_hotkey = next(f for f in features if f.kind == "hotkey" and f.details.get("trigger") == "q")
        console_hotkey = next(f for f in features if f.kind == "hotkey" and f.details.get("trigger") == "^v")
        suspend = next(f for f in features if f.kind == "lifecycle-operation")

        self.assertEqual("implemented", macro.coverage["implementation"])
        self.assertEqual("regression", macro.coverage["scenario"])
        self.assertEqual("implemented", clipboard.coverage["implementation"])
        self.assertEqual("regression", clipboard.coverage["scenario"])
        self.assertEqual("implemented", macro_function.coverage["implementation"])
        self.assertEqual("regression", macro_function.coverage["scenario"])
        self.assertEqual("real-windows:#59", macro_ui.coverage["scenario"])
        self.assertEqual("implemented", window.coverage["implementation"])
        self.assertEqual("regression", window.coverage["scenario"])
        self.assertEqual("real-windows:#59", clipboard_label.coverage["scenario"])
        self.assertEqual("implemented", global_hotkey.coverage["implementation"])
        self.assertEqual("regression", global_hotkey.coverage["scenario"])
        self.assertEqual("deferred:#57", console_hotkey.coverage["implementation"])
        self.assertEqual("deferred:#57", console_hotkey.coverage["scenario"])
        self.assertEqual("implemented", suspend.coverage["implementation"])
        self.assertEqual("regression", suspend.coverage["scenario"])

        self.assertEqual("real-windows-verification-required", macro.classification)
        self.assertEqual("real-windows-verification-required", clipboard.classification)
        self.assertEqual("real-windows-verification-required", macro_function.classification)
        self.assertEqual("real-windows-verification-required", macro_ui.classification)
        self.assertEqual("real-windows-verification-required", window.classification)
        self.assertEqual("real-windows-verification-required", clipboard_label.classification)
        self.assertEqual("partially-verified", global_hotkey.classification)
        self.assertEqual("real-windows-verification-required", console_hotkey.classification)
        self.assertEqual("partially-verified", suspend.classification)

    def test_required_real_windows_is_distinct_from_unverified(self):
        coverage = {
            "implementation": "implemented",
            "unit": "covered",
            "scenario": "covered",
            "exeDiff": "covered",
            "ahkDiff": "covered",
            "realWindows": "required",
            "intentionalDifference": "no",
        }
        self.assertEqual("real-windows-verification-required", module.derive_classification(coverage))
        coverage["realWindows"] = "unverified"
        self.assertEqual("partially-verified", module.derive_classification(coverage))

    def test_report_and_markdown_include_unknown_count(self):
        source, features = self.scan()
        module.apply_coverage(features, {})
        report = module.build_report(
            source,
            features,
            {"count": 0, "files": [], "inventoryIds": []},
            {"available": False},
        )
        self.assertEqual(len(features), report["summary"]["unknownCount"])
        rendered = module.markdown(report)
        self.assertIn("# hotkeySKG compatibility matrix", rendered)
        self.assertIn("Unknown:", rendered)


if __name__ == "__main__":
    unittest.main()
