from pathlib import Path

p = Path("tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs")
text = p.read_text(encoding="utf-8")
old = "using iKeyd.Core.Input;\nusing Xunit;"
new = "using iKeyd.Core.Input;\nusing iKeyd.Windows.Input;\nusing Xunit;"
if text.count(old) != 1:
    raise SystemExit(f"expected one import insertion point, found {text.count(old)}")
p.write_text(text.replace(old, new, 1), encoding="utf-8")
