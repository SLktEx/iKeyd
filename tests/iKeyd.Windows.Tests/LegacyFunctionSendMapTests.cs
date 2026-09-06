using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Profiles.HotkeySkg.Layers;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyFunctionSendMapTests
{
    public static TheoryData<KeyCode, string, string, string, string> PinnedValues => new()
    {
        { KeyCode.Q, "(", "\"", "'", "" },
        { KeyCode.W, "!{F4}", "^{F4}", "", "" },
        { KeyCode.U, "{HOME}", "+{HOME}", "^{HOME}", "^+{HOME}" },
        { KeyCode.I, "{UP}", "+{UP}", "^{UP}", "^+{UP}" },
        { KeyCode.O, "{END}", "+{END}", "^{END}", "^+{END}" },
        { KeyCode.P, "{PGUP}", "+{PGUP}", "^{PGUP}", "^+{PGUP}" },
        { KeyCode.At, "{ESC}", "{AppsKey}", "!{Space}", "" },
        { KeyCode.A, "[]{LEFT}", "{{}", "{{}{ENTER}{ENTER}{}}{UP}{END}", "" },
        { KeyCode.S, "(){LEFT}", "{}}", "{{}{}}{LEFT}", "" },
        { KeyCode.D, "-", "=", "%", "~" },
        { KeyCode.J, "{LEFT}", "+{LEFT}", "^{LEFT}", "^+{LEFT}" },
        { KeyCode.K, "{DOWN}", "+{DOWN}", "^{DOWN}", "^+{DOWN}" },
        { KeyCode.L, "{RIGHT}", "+{RIGHT}", "^{RIGHT}", "^+{RIGHT}" },
        { KeyCode.SColon, "{PGDN}", "+{PGDN}", "^{PGDN}", "^+{PGDN}" },
        { KeyCode.Colon, "$", "{#}", "&", "{^}" },
        { KeyCode.Z, "\\", "/", "|", "" },
        { KeyCode.X, "\"\"{LEFT}", "''{LEFT}", "%%{LEFT}", "" },
        { KeyCode.C, "_", ">", "<", "" },
        { KeyCode.N, "{BS}", "!{RIGHT}", "!{LEFT}", "^+n" },
        { KeyCode.M, "{DEL}", "{END}{SHIFT DOWN}{HOME}{LEFT}{SHIFT UP}", "{HOME}+{END}", "#m" },
        { KeyCode.Comma, "{SPACE}", "{TAB}", "{ENTER}", "" },
        { KeyCode.Dot, ")", "<>{LEFT}", "</>{LEFT}", "" },
        { KeyCode.Slash, "{!}", "/*  */{LEFT 3}", "{END}+{HOME}^x\\begin{{}^v{}}{ENTER 2}\\end{{}^v{}}{UP}", "" },
        { KeyCode.Digit5, "", "", "", "" },
        { KeyCode.Digit6, "", "", "", "" },
        { KeyCode.Digit7, "", "", "", "" },
        { KeyCode.Digit8, "", "", "", "" },
        { KeyCode.Digit9, "", "", "", "" },
        { KeyCode.Digit0, "", "", "", "" },
    };

    [Theory]
    [MemberData(nameof(PinnedValues))]
    public void Direct_table_matches_pinned_source_values(
        KeyCode key,
        string m,
        string mh,
        string hm,
        string ms)
    {
        Assert.True(LegacyFunctionSendMap.TryGetValues(key, out var actual));
        Assert.Equal(new LegacyFunctionSendMap.Values(m, mh, hm, ms), actual);
    }

    [Fact]
    public void Ordered_layer_state_selects_the_exact_legacy_argument()
    {
        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.U,
            LayerState.FromSequence(LayerKey.M, LayerKey.S),
            out var ms));
        Assert.Equal("^+{HOME}", ms);

        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.U,
            LayerState.FromSequence(LayerKey.H, LayerKey.M),
            out var hm));
        Assert.Equal("^{HOME}", hm);
    }

    [Fact]
    public void K_and_A_composites_apply_the_legacy_outer_modifier_once_at_initialization()
    {
        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.U,
            LayerState.FromSequence(LayerKey.K, LayerKey.M),
            out var km));
        Assert.Equal("^{HOME}", km);

        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.U,
            LayerState.FromSequence(LayerKey.K, LayerKey.M, LayerKey.H),
            out var kmh));
        Assert.Equal("^+{HOME}", kmh);

        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.U,
            LayerState.FromSequence(LayerKey.K, LayerKey.H, LayerKey.M),
            out var khm));
        Assert.Equal("^^{HOME}", khm);

        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.U,
            LayerState.FromSequence(LayerKey.A, LayerKey.M, LayerKey.S),
            out var ams));
        Assert.Equal("!^+{HOME}", ams);
    }

    [Fact]
    public void Empty_legacy_arguments_are_still_handled_even_with_K_or_A_prefix()
    {
        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.Digit5,
            LayerState.FromSequence(LayerKey.K, LayerKey.M),
            out var km));
        Assert.Empty(km);

        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.Digit5,
            LayerState.FromSequence(LayerKey.A, LayerKey.M),
            out var am));
        Assert.Empty(am);
    }

    [Fact]
    public void Hot_path_lookup_allocates_nothing_after_static_initialization()
    {
        var state = LayerState.FromSequence(LayerKey.K, LayerKey.M, LayerKey.H);
        Assert.True(LegacyFunctionSendMap.TryResolve(KeyCode.Slash, state, out _));

        var before = GC.GetAllocatedBytesForCurrentThread();
        string? last = null;
        var allResolved = true;
        for (var i = 0; i < 10_000; i++)
        {
            allResolved &= LegacyFunctionSendMap.TryResolve(KeyCode.Slash, state, out var output);
            last = output;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(last);
        Assert.True(allResolved);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Process_keys_do_not_enter_the_direct_table()
    {
        Assert.False(LegacyFunctionSendMap.TryGetValues(KeyCode.E, out _));
        Assert.False(LegacyFunctionSendMap.TryGetValues(KeyCode.R, out _));
        Assert.False(LegacyFunctionSendMap.TryGetValues(KeyCode.T, out _));
    }
}
