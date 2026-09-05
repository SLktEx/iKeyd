using iKeyd.Core.Macros;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class MacroParserTests
{
    [Fact]
    public void Parser_recognizes_macro_directives_and_preserves_legacy_send_braces()
    {
        var parser = new MacroParser();

        var program = parser.Parse("a{UP}{Wait 100}{calc (1+2)*3}{hk MHr}{Click,1,2,Right,Down}");

        Assert.Collection(
            program.Nodes,
            node => Assert.Equal("a{UP}", Assert.IsType<MacroText>(node).Text),
            node => Assert.Equal(TimeSpan.FromMilliseconds(100), Assert.IsType<MacroWait>(node).Duration),
            node => Assert.Equal("(1+2)*3", Assert.IsType<MacroCalc>(node).Expression),
            node =>
            {
                var hotkey = Assert.IsType<MacroHotkey>(node);
                Assert.Equal("MH", hotkey.State);
                Assert.Equal('r', hotkey.Key);
            },
            node => Assert.Equal("{Click,1,2,Right,Down}", Assert.IsType<MacroText>(node).Text));
    }

    [Theory]
    [InlineData("{wait nope}")]
    [InlineData("{wait -1}")]
    [InlineData("{hk Xr}")]
    [InlineData("{hk M1}")]
    public void Parser_rejects_invalid_directives(string source)
        => Assert.Throws<MacroParseException>(() => new MacroParser().Parse(source));

    [Theory]
    [InlineData("(1+2)*3", 9)]
    [InlineData("7/2", 3)]
    [InlineData("7%4", 3)]
    [InlineData("2^3^2", 64)]
    [InlineData("2+3*4", 14)]
    [InlineData("-(2+3)*4", -20)]
    public void Calculator_matches_integer_macro_semantics(string expression, long expected)
        => Assert.Equal(expected, new MacroExpressionEvaluator().Evaluate(expression));

    [Fact]
    public void Calculator_rejects_division_by_zero()
        => Assert.Throws<FormatException>(() => new MacroExpressionEvaluator().Evaluate("1/0"));

    [Fact]
    public void Incrementer_removes_markers_for_current_iteration_and_updates_marked_fields()
    {
        var incrementer = new MacroIncrementer();

        var numeric = incrementer.PrepareIteration("item`9`");
        Assert.Equal("item9", numeric.RenderedTemplate);
        Assert.Equal("item`10`", numeric.NextTemplate);

        var text = incrementer.PrepareIteration("x`az`z");
        Assert.Equal("xazz", text.RenderedTemplate);
        Assert.Equal("x`b`z", text.NextTemplate);
    }

    [Theory]
    [InlineData("3", 3, false)]
    [InlineData("+3", 3, true)]
    [InlineData("0", 0, false)]
    public void Repeat_parser_supports_legacy_plus_prefix(string source, int count, bool persist)
    {
        var repeat = MacroRepeat.Parse(source);
        Assert.Equal(count, repeat.Count);
        Assert.Equal(persist, repeat.Persist);
    }
}
