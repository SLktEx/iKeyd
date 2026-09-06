from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd.py"
spec = importlib.util.spec_from_file_location("ikeyd_mouse_dsl", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class MouseDslTests(unittest.TestCase):
    def test_mouse_block_compiles_to_profile_json(self):
        text = """
profile demo {
    chord_window = 40ms
}
mouse {
    engine = virtual_stick
    update = 4ms

    response {
        press = 35ms
        release = 1ms
        curve = linear
    }

    speed {
        normal = 1800
        precision = 500px/s
        fine = 120
        fast = 3600.5
    }

    socd = neutral
    tap_nudge = 2px
    max_catchup = 24ms
}
keymap S {
}
keymap K {
}
""".strip()
        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertEqual(
            {
                "engine": "virtual_stick",
                "updateMs": 4,
                "response": {"pressMs": 35, "releaseMs": 1, "curve": "linear"},
                "speed": {"normal": 1800, "precision": 500, "fine": 120, "fast": 3600.5},
                "socd": "neutral",
                "tapNudgePixels": 2,
                "maxCatchupMs": 24,
            },
            profile["mouse"],
        )

    def test_mouse_block_uses_runtime_defaults_for_omitted_values(self):
        text = """
profile demo {
    chord_window = 40ms
}
mouse {
    speed {
        normal = 1234
    }
}
keymap S {
}
""".strip()
        mouse = module.compile_dsl(text, Path("profile.ikeyd"))["mouse"]
        self.assertEqual(8, mouse["updateMs"])
        self.assertEqual({"pressMs": 45, "releaseMs": 2, "curve": "smoothstep"}, mouse["response"])
        self.assertEqual(1234, mouse["speed"]["normal"])
        self.assertEqual(800, mouse["speed"]["precision"])
        self.assertEqual(1, mouse["tapNudgePixels"])

    def test_profile_without_mouse_keeps_legacy_json_shape(self):
        text = """
profile demo {
    chord_window = 40ms
}
keymap S {
}
""".strip()
        profile = module.compile_dsl(text, Path("profile.ikeyd"))
        self.assertNotIn("mouse", profile)

    def test_unknown_mouse_setting_reports_source_line(self):
        text = """
profile demo {
    chord_window = 40ms
}
mouse {
    magic = true
}
keymap S {
}
""".strip()
        with self.assertRaisesRegex(module.DslError, r"profile\.ikeyd:5: unknown mouse setting 'magic'"):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_duplicate_mouse_setting_is_rejected(self):
        text = """
profile demo {
    chord_window = 40ms
}
mouse {
    update = 8ms
    update = 4ms
}
keymap S {
}
""".strip()
        with self.assertRaisesRegex(module.DslError, r"duplicate mouse setting 'update'"):
            module.compile_dsl(text, Path("profile.ikeyd"))


if __name__ == "__main__":
    unittest.main()
