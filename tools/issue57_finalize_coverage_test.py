from pathlib import Path

path = Path("tests/tooling/test_legacy_compatibility_inventory.py")
text = path.read_text(encoding="utf-8")
old = '        self.assertEqual("deferred:#57", window.coverage["scenario"])'
new = '        self.assertEqual("regression", window.coverage["scenario"])'
if text.count(old) != 1:
    raise SystemExit(f"expected exactly one stale #57 window assertion, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
