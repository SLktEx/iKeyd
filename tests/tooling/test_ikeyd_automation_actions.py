from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd-dsl.py"
spec = importlib.util.spec_from_file_location("ikeyd_dsl_automation", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class IKeydAutomationDslTests(unittest.TestCase):
    def test_exec_shell_and_query_compile_losslessly(self):
        text = r'''
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layer TOOLS {
    POS.Q = exec("tool.exe", "hello world", "&literal", "quoted\"value")
    POS.W = shell("echo hello && echo world")
    POS.E = query(foreground.process)
}
keymap S {
    Q = q
}
keymap K {
    Q = q
}
'''.strip()

        profile = module.compile_dsl(text, Path("automation.ikeyd"))

        self.assertEqual(
            {"kind": "exec", "value": "tool.exe", "args": ["hello world", "&literal", 'quoted"value']},
            profile["layers"]["TOOLS"]["Q"],
        )
        self.assertEqual(
            {"kind": "shell", "value": "echo hello && echo world"},
            profile["layers"]["TOOLS"]["W"],
        )
        self.assertEqual(
            {"kind": "query", "value": "foreground.process"},
            profile["layers"]["TOOLS"]["E"],
        )

    def test_unknown_query_is_rejected(self):
        text = '''
profile demo {
    chord_window = 40ms
}
keyboard JIS109
layer TOOLS {
    POS.Q = query(system.magic)
}
keymap S {
    Q = q
}
keymap K {
    Q = q
}
'''.strip()

        with self.assertRaisesRegex(module.DslError, r"unknown system query 'system\.magic'"):
            module.compile_dsl(text, Path("automation.ikeyd"))


if __name__ == "__main__":
    unittest.main()
