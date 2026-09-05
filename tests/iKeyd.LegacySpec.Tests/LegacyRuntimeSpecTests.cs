using System.Text.Json;
using Xunit;

namespace iKeyd.LegacySpec.Tests;

public sealed class LegacyRuntimeSpecTests
{
    private static readonly JsonDocument Runtime = LoadFixture("hotkeySKG.runtime.json");
    private static JsonElement Root => Runtime.RootElement;

    [Fact]
    public void IME_romaji_kana_detection_matches_legacy_conversion_modes()
    {
        var modes = Root.GetProperty("ime")
            .GetProperty("romaKanaConversionModes")
            .EnumerateArray()
            .Select(x => x.GetInt32())
            .ToArray();

        Assert.Equal(new[] { 9, 19, 25, 27, 16 }, modes);
        Assert.True(Root.GetProperty("ime").GetProperty("requiresImeOpen").GetBoolean());
    }

    [Fact]
    public void Input_routing_preserves_the_TMODE_gimode_inheritance_quirk()
    {
        var cases = Root.GetProperty("ime").GetProperty("routingCases").EnumerateArray().ToArray();

        AssertCase(cases, "S", "S", true, "ChordEngine:S");
        AssertCase(cases, "S", "S", false, "PassThrough");
        AssertCase(cases, "K", "K", true, "ChordEngine:K");
        AssertCase(cases, "R", "", true, "PassThrough");
        AssertCase(cases, "T", "S", false, "ChordEngine:S");
        AssertCase(cases, "T", "K", false, "ChordEngine:K");
        AssertCase(cases, "T", "", false, "ChordEngine:");
    }

    [Fact]
    public void Mode_commands_capture_the_legacy_gmode_and_gimode_pair()
    {
        var commands = Root.GetProperty("modeCommands").EnumerateArray().ToArray();
        AssertMode(commands, "process1", "S", "S");
        AssertMode(commands, "process2", "R", "");
        AssertMode(commands, "process3", "T", "unchanged");
        AssertMode(commands, "process4", "K", "K");
    }

    [Fact]
    public void Representative_layer_transitions_are_frozen()
    {
        var cases = Root.GetProperty("layerCases").EnumerateArray().ToArray();

        AssertLayer(cases, "m-then-h", "M", 0, "HDown", "MH", 0, []);
        AssertLayer(cases, "h-then-m", "H", 0, "MDown", "HM", 0, []);
        AssertLayer(cases, "mh-h-up-tap", "MH", 0, "HUp", "M", 1, ["Tab"]);
        AssertLayer(cases, "hm-m-up-tap", "HM", 0, "MUp", "H", 1, ["Shift+Tab"]);
        AssertLayer(cases, "ms-space-up", "MS", 0, "SpaceUp", "M", 1, ["Enter"]);
        AssertLayer(cases, "kms-space-up", "KMS", 0, "SpaceUp", "M", 1, ["Ctrl+Enter"]);
        AssertLayer(cases, "kana-empty", "", 0, "KanaDown", "K", 0, []);
        AssertLayer(cases, "kana-k-toggle-off", "K", 0, "KanaDown", "", 0, []);
        AssertLayer(cases, "kana-m", "M", 0, "KanaDown", "M", 1, ["Muhenkan"]);
        AssertLayer(cases, "kana-h", "H", 0, "KanaDown", "H", 1, ["Henkan"]);
        AssertLayer(cases, "kana-s", "S", 0, "KanaDown", "S", 1, ["Ctrl+Esc"]);
    }

    [Fact]
    public void Modified_key_dispatch_keeps_legacy_special_cases()
    {
        var cases = Root.GetProperty("modifiedKeyDispatch").EnumerateArray().ToArray();

        AssertDispatch(cases, "K", "Win+key", true);
        AssertDispatch(cases, "A", "Alt+key", true);
        AssertDispatch(cases, "SH", "SHKey", false);
        AssertDispatch(cases, "KSH", "Ctrl+SHKey", false);
        AssertDispatch(cases, "ASH", "Alt+SHKey", false);
        AssertDispatch(cases, "SM", "MouseMediaMode", false);
    }

    [Fact]
    public void Representative_function_and_desktop_actions_are_frozen()
    {
        var functions = Root.GetProperty("functionKeyCases").EnumerateArray().ToArray();
        AssertFunction(functions, "Q", "M", "(");
        AssertFunction(functions, "W", "M", "Alt+F4");
        AssertFunction(functions, "J", "MS", "Ctrl+Shift+Left");
        AssertFunction(functions, "Comma", "HM", "Enter");

        var desktop = Root.GetProperty("desktopCases").EnumerateArray().ToArray();
        AssertDesktop(desktop, "E", "M", "Minimize");
        AssertDesktop(desktop, "R", "M", "ToggleMaximizeRestore");
        AssertDesktop(desktop, "R", "MS", "Win+R");
        AssertDesktop(desktop, "T", "M", "ToggleTopmost");
        AssertDesktop(desktop, "G", "MH", "Ctrl+Tab");
        AssertDesktop(desktop, "B", "HM", "Alt+Shift+Esc");
    }

    [Fact]
    public void Mouse_media_mode_and_move_amounts_are_frozen()
    {
        var cases = Root.GetProperty("mouseMediaCases").EnumerateArray().ToArray();
        AssertMouseMedia(cases, "j", "MouseLeft");
        AssertMouseMedia(cases, "u", "LeftClick");
        AssertMouseMedia(cases, "q", "VolumeUp");
        AssertMouseMedia(cases, "f", "MediaPlayPause");

        var amounts = Root.GetProperty("mouseMoveAmounts").EnumerateArray().ToArray();
        AssertAmount(amounts, "D", "30");
        AssertAmount(amounts, "E", "10");
        AssertAmount(amounts, "C", "ScreenQuarter");
        AssertAmount(amounts, "none", "100");
    }

    [Fact]
    public void Clipboard_history_contract_captures_capacity_prepend_and_selection_sensitive_insert()
    {
        var clipboard = Root.GetProperty("clipboard");
        Assert.Equal(20, clipboard.GetProperty("capacity").GetInt32());
        Assert.True(clipboard.GetProperty("emptyClipboardIgnored").GetBoolean());
        Assert.Equal(0, clipboard.GetProperty("newItemAtIndex").GetInt32());
        Assert.Equal("Shift+Insert", clipboard.GetProperty("pasteAction").GetString());

        var cases = clipboard.GetProperty("cases").EnumerateArray().ToArray();
        AssertClipboard(cases, "first", ["A"]);
        AssertClipboard(cases, "prepend", ["B", "A"]);
        AssertClipboard(cases, "after-selecting-second", ["C", "B"]);
    }

    [Fact]
    public void Macro_contract_captures_tokens_integer_math_and_increment_behavior()
    {
        var macro = Root.GetProperty("macro");
        var features = macro.GetProperty("features").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("wait", features);
        Assert.Contains("calc", features);
        Assert.Contains("hk", features);
        Assert.Contains("increment", features);
        Assert.Contains("escape-cancel", features);

        var calcCases = macro.GetProperty("calculatorCases").EnumerateArray().ToArray();
        AssertCalculator(calcCases, "(1+2)*3", "9");
        AssertCalculator(calcCases, "2^3*4", "32");
        AssertCalculator(calcCases, "5/2", "2");
        AssertCalculator(calcCases, "7%4", "3");

        Assert.Equal("[MSH]+", macro.GetProperty("hkStatePattern").GetString());
    }

    [Fact]
    public void Known_source_quirks_are_explicit_not_silently_normalized()
    {
        var quirks = Root.GetProperty("knownQuirks").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();

        Assert.Contains(quirks, q => q.Contains("TMODE", StringComparison.Ordinal));
        Assert.Contains(quirks, q => q.Contains("KSH", StringComparison.Ordinal));
        Assert.Contains(quirks, q => q.Contains("s tate", StringComparison.Ordinal));
        Assert.Contains(quirks, q => q.Contains("selection-sensitive", StringComparison.Ordinal));
    }

    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void AssertCase(JsonElement[] cases, string gmode, string gimode, bool ime, string expected)
    {
        var item = Assert.Single(cases.Where(x =>
            x.GetProperty("gmode").GetString() == gmode &&
            x.GetProperty("gimode").GetString() == gimode &&
            x.GetProperty("imeRomaKana").GetBoolean() == ime));
        Assert.Equal(expected, item.GetProperty("expected").GetString());
    }

    private static void AssertMode(JsonElement[] cases, string command, string gmode, string gimode)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("command").GetString() == command));
        Assert.Equal(gmode, item.GetProperty("resultGmode").GetString());
        Assert.Equal(gimode, item.GetProperty("resultGimode").GetString());
    }

    private static void AssertLayer(JsonElement[] cases, string name, string initialState, int initialFlag, string @event, string finalState, int finalFlag, string[] actions)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("name").GetString() == name));
        Assert.Equal(initialState, item.GetProperty("initialState").GetString());
        Assert.Equal(initialFlag, item.GetProperty("initialFlag").GetInt32());
        Assert.Equal(@event, item.GetProperty("event").GetString());
        Assert.Equal(finalState, item.GetProperty("finalState").GetString());
        Assert.Equal(finalFlag, item.GetProperty("finalFlag").GetInt32());
        Assert.Equal(actions, item.GetProperty("actions").EnumerateArray().Select(x => x.GetString() ?? "").ToArray());
    }

    private static void AssertDispatch(JsonElement[] cases, string state, string action, bool clears)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("state").GetString() == state));
        Assert.Equal(action, item.GetProperty("action").GetString());
        Assert.Equal(clears, item.GetProperty("clearsState").GetBoolean());
    }

    private static void AssertFunction(JsonElement[] cases, string key, string state, string action)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("key").GetString() == key && x.GetProperty("state").GetString() == state));
        Assert.Equal(action, item.GetProperty("action").GetString());
    }

    private static void AssertDesktop(JsonElement[] cases, string function, string state, string action)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("function").GetString() == function && x.GetProperty("state").GetString() == state));
        Assert.Equal(action, item.GetProperty("modernAction").GetString());
    }

    private static void AssertMouseMedia(JsonElement[] cases, string key, string action)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("key").GetString() == key));
        Assert.Equal(action, item.GetProperty("action").GetString());
    }

    private static void AssertAmount(JsonElement[] cases, string heldKey, string expected)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("heldKey").GetString() == heldKey));
        Assert.Equal(expected, item.GetProperty("amount").ToString());
    }

    private static void AssertClipboard(JsonElement[] cases, string name, string[] expected)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("name").GetString() == name));
        Assert.Equal(expected, item.GetProperty("expected").EnumerateArray().Select(x => x.GetString() ?? "").ToArray());
    }

    private static void AssertCalculator(JsonElement[] cases, string expression, string expected)
    {
        var item = Assert.Single(cases.Where(x => x.GetProperty("expression").GetString() == expression));
        Assert.Equal(expected, item.GetProperty("expected").GetString());
    }
}
