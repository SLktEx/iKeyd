from __future__ import annotations

import importlib.util
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
singleStrokeS_Q=-
flag_Q:=1<<1
flag_W:=1<<2
kCmbS1:=flag_Q|flag_W
resultOfKCmbS1=foo
IME_IfRomaKana(){
  imeget := DllCall("SendMessage")
}
#IfWinActive ahk_class ConsoleWindowClass
^v::Send,!{Space}ep
#IfWinActive
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
        self.assertIn("mouse-operation", kinds)
        self.assertIn("label", kinds)

    def test_tracks_window_context_and_key_up(self):
        _, features = self.scan()
        console = next(f for f in features if f.kind == "hotkey" and f.details["trigger"] == "^v")
        self.assertEqual("#IfWinActive ahk_class ConsoleWindowClass", console.window_context)
        self.assertIn("process-specific", console.tags)
        key_up = next(f for f in features if f.kind == "hotkey" and f.details["trigger"].lower().endswith(" up"))
        self.assertIn("input-state", key_up.tags)

    def test_chord_records_following_result(self):
        _, features = self.scan()
        chord = next(f for f in features if f.kind == "chord")
        self.assertEqual(["Q", "W"], chord.details["keys"])
        self.assertEqual("foo", chord.details["output"])

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
        self.assertEqual("real-windows-verification-required", single.classification)
        self.assertEqual(["fixture"], single.evidence)
        function = next(f for f in features if f.kind == "function")
        self.assertEqual("unknown", function.classification)

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
