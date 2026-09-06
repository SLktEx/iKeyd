from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "compile-ikeyd-dsl.py"
spec = importlib.util.spec_from_file_location("ikeyd_clipboard_dsl", SCRIPT)
module = importlib.util.module_from_spec(spec)
assert spec and spec.loader
sys.modules[spec.name] = module
spec.loader.exec_module(module)


BASE = """
profile demo {
    chord_window = 40ms
}
{clipboard}
keymap S {
    A = "a"
}
keymap K {
    A = "a"
}
""".strip()


class ClipboardDslTests(unittest.TestCase):
    def test_clipboard_block_compiles_to_runtime_profile(self):
        text = BASE.format(
            clipboard="""clipboard {
    history = true
    max_items = 128
    persist = false
    images = false
    encryption = user
    cipher = chacha20_poly1305
    directory = \"%LOCALAPPDATA%\\\\iKeyd-test\"
}"""
        )

        profile = module.compile_dsl(text, Path("profile.ikeyd"))

        self.assertEqual(
            {
                "history": True,
                "maxItems": 128,
                "persist": False,
                "images": False,
                "encryption": "user",
                "cipher": "chacha20-poly1305",
                "directory": "%LOCALAPPDATA%\\iKeyd-test",
            },
            profile["clipboard"],
        )

    def test_empty_clipboard_block_uses_compatible_defaults(self):
        profile = module.compile_dsl(
            BASE.format(clipboard="clipboard {\n}"),
            Path("profile.ikeyd"),
        )

        self.assertEqual(
            {
                "history": True,
                "maxItems": 20,
                "persist": True,
                "images": True,
                "encryption": "user",
                "cipher": "auto",
            },
            profile["clipboard"],
        )

    def test_missing_clipboard_block_does_not_change_existing_profile_shape(self):
        profile = module.compile_dsl(
            BASE.format(clipboard=""),
            Path("profile.ikeyd"),
        )
        self.assertNotIn("clipboard", profile)

    def test_invalid_max_items_is_rejected(self):
        text = BASE.format(clipboard="clipboard {\n    max_items = 0\n}")
        with self.assertRaisesRegex(module.DslError, "clipboard.max_items must be a positive integer"):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_unknown_cipher_is_rejected(self):
        text = BASE.format(clipboard="clipboard {\n    cipher = magic\n}")
        with self.assertRaisesRegex(module.DslError, "clipboard.cipher currently supports"):
            module.compile_dsl(text, Path("profile.ikeyd"))

    def test_duplicate_setting_is_rejected(self):
        text = BASE.format(
            clipboard="clipboard {\n    images = true\n    images = false\n}"
        )
        with self.assertRaisesRegex(module.DslError, "duplicate clipboard setting 'images'"):
            module.compile_dsl(text, Path("profile.ikeyd"))


if __name__ == "__main__":
    unittest.main()
