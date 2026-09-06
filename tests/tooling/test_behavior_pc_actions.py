from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd-dsl.py"
spec = importlib.util.spec_from_file_location("ikeyd_dsl_pc_actions", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class BehaviorPcActionDslTests(unittest.TestCase):
    def test_pc_action_payloads_use_existing_behavior_option_blocks(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap S {
    H = MOUSE_MOVE() {
        x = -30
        y = 10
    }
    I = TEXT() {
        value = "^+{}"
    }
    O = MACRO() {
        template = "hello, world"
    }
    P = MEDIA(PlayPause)
}
keymap K {
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        behaviors = profile["behaviors"]["S"]

        self.assertEqual(
            {
                "name": "MOUSE_MOVE",
                "arguments": [],
                "options": {"x": "-30", "y": "10"},
            },
            behaviors["H"],
        )
        self.assertEqual(
            {
                "name": "TEXT",
                "arguments": [],
                "options": {"value": "^+{}"},
            },
            behaviors["I"],
        )
        self.assertEqual(
            {
                "name": "MACRO",
                "arguments": [],
                "options": {"template": "hello, world"},
            },
            behaviors["O"],
        )
        self.assertEqual(
            {"name": "MEDIA", "arguments": ["PlayPause"]},
            behaviors["P"],
        )

    def test_user_defined_behavior_and_pc_helpers_can_coexist(self):
        text = """
profile demo {
    chord_window = 40ms
}
behavior CUSTOM(key) {
    var used: bool = false
    on_press {
        used = true
        send key
    }
}
keymap S {
    A = CUSTOM(B)
    H = MO(NAV)
}
keymap K {
}
keymap NAV {
    J = "left"
}
""".strip()

        profile = module.compile_dsl(text, Path("profile.ikeyd"))

        self.assertIn("CUSTOM", profile["behaviorDefinitions"])
        self.assertEqual("CUSTOM", profile["behaviors"]["S"]["A"]["name"])
        self.assertEqual("MO", profile["behaviors"]["S"]["H"]["name"])


if __name__ == "__main__":
    unittest.main()
