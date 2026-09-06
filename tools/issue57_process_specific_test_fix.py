from pathlib import Path

p = Path("tests/iKeyd.Windows.Tests/ProcessSpecificRuntimeCompatibilityTests.cs")
text = p.read_text(encoding="utf-8")


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected one match, found {count}: {old[:100]!r}")
    text = text.replace(old, new, 1)

replace_once(
    "using iKeyd.Core.Input;\nusing Xunit;",
    "using iKeyd.Core.Input;\nusing iKeyd.Windows.Input;\nusing Xunit;",
)
replace_once(
    "    public void Console_ctrl_hotkeys_emit_legacy_system_menu_sequence(char input, char finalKey)",
    "    public void Console_ctrl_hotkeys_emit_legacy_system_menu_sequence(char input, char finalKey)",
)
replace_once(
    "        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)'E' && item.Kind == KeyEventKind.Down);\n        Assert.Contains(keyboard.Events, item => item.Key.VirtualKey == (ushort)finalKey && item.Kind == KeyEventKind.Down);",
    "        Assert.Equal($\"e{char.ToLowerInvariant(finalKey)}\", keyboard.Text);",
)
replace_once(
    "        public List<KeyboardEvent> Events { get; } = [];\n        public void SendKey(KeyboardKey key, KeyEventKind kind)",
    "        public List<KeyboardEvent> Events { get; } = [];\n        public string Text { get; private set; } = string.Empty;\n        public void SendKey(KeyboardKey key, KeyEventKind kind)",
)
replace_once(
    "        public void SendText(string text) { }",
    "        public void SendText(string text) => Text += text;",
)

p.write_text(text, encoding="utf-8")
