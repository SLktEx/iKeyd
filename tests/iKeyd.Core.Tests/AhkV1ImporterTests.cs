using iKeyd.Core.Importing;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class AhkV1ImporterTests
{
    [Fact]
    public void Imports_single_chord_and_simple_hotkeys_into_automation_profile()
    {
        const string source = """
            flag_Q:=1<<1
            flag_W:=1<<2
            singleStrokeS_Q=ka
            singleStrokeS_W=na
            kCmbS1:=flag_Q|flag_W
            resultOfKCmbS1=kya
            ^j::Send, hello
            !k::processSomething
            """;

        var result = new AhkV1Importer().Import(source, chordWindowMs: 55);

        Assert.False(result.HasErrors);
        Assert.Equal(55, result.Profile.ChordWindowMs);
        Assert.Equal("S", result.Profile.StartupMode);
        var keymap = result.Profile.GetKeymap("S");
        Assert.Collection(
            keymap.SingleMappings,
            item => { Assert.Equal("Q", item.Key.Value); Assert.Equal("ka", item.Output); },
            item => { Assert.Equal("W", item.Key.Value); Assert.Equal("na", item.Output); });
        var chord = Assert.Single(keymap.ChordMappings);
        Assert.Equal("Q", chord.First.Value);
        Assert.Equal("W", chord.Second.Value);
        Assert.Equal("kya", chord.Output);

        Assert.Collection(
            result.Profile.Hotkeys,
            item => { Assert.Equal("^j", item.Trigger); Assert.Equal("Send, hello", item.Action); },
            item => { Assert.Equal("!k", item.Trigger); Assert.Equal("processSomething", item.Action); });
    }

    [Fact]
    public void Preserves_legacy_duplicate_semantics_and_reports_them()
    {
        const string source = """
            singleStrokeK_A=first
            singleStrokeK_A=second
            kCmbK1:=flag_A|flag_B
            resultOfKCmbK1=firstChord
            kCmbK2:=flag_B|flag_A
            resultOfKCmbK2=secondChord
            """;

        var result = new AhkV1Importer().Import(source);
        var keymap = result.Profile.GetKeymap("K");

        var single = Assert.Single(keymap.SingleMappings);
        Assert.Equal("second", single.Output);
        Assert.Equal(2, keymap.ChordMappings.Count);
        Assert.Equal("firstChord", keymap.ChordMappings[0].Output);
        Assert.Equal("secondChord", keymap.ChordMappings[1].Output);
        Assert.Contains(result.Diagnostics, item => item.Code == "AHK2001" && item.Severity == ImportDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, item => item.Code == "AHK2002" && item.Severity == ImportDiagnosticSeverity.Warning);

        var compiled = keymap.BuildKeymap();
        Assert.True(compiled.TryGetChord("A", "B", out var effective));
        Assert.Equal("firstChord", effective);
    }

    [Fact]
    public void Reports_unsupported_and_multiline_hotkey_with_source_lines()
    {
        const string source = """
            MsgBox, hello
            ^a::
            Send, multiline
            """;

        var result = new AhkV1Importer().Import(source);

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.UnsupportedStatementCount);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == AhkV1Importer.UnsupportedStatementCode &&
            item.Line == 1 &&
            item.SourceText == "MsgBox, hello");
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "AHK1004" &&
            item.Line == 2 &&
            item.Severity == ImportDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Missing_chord_result_is_an_error_and_not_emitted()
    {
        const string source = "kCmbS9:=flag_Q|flag_W";

        var result = new AhkV1Importer().Import(source);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "AHK1003" &&
            item.Line == 1 &&
            item.Severity == ImportDiagnosticSeverity.Error);
        Assert.Empty(result.Profile.GetKeymap("S").ChordMappings);
    }

    [Fact]
    public void Malformed_mapping_is_reported_as_error_instead_of_silently_ignored()
    {
        const string source = "singleStrokeS-=oops";

        var result = new AhkV1Importer().Import(source);

        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("AHK1001", diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(ImportDiagnosticSeverity.Error, diagnostic.Severity);
    }
}
