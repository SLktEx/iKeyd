using System.Globalization;

namespace iKeyd.Core.Macros;

public sealed class MacroParser
{
    private static readonly HashSet<char> HotkeyPunctuation = ['@', ';', ':', ',', '.', '/'];
    private readonly MacroLexer _lexer;

    public MacroParser(MacroLexer? lexer = null)
        => _lexer = lexer ?? new MacroLexer();

    public MacroProgram Parse(string source)
    {
        var tokens = _lexer.Lex(source);
        var nodes = new List<MacroNode>(tokens.Count);

        foreach (var token in tokens)
        {
            nodes.Add(token.Kind switch
            {
                MacroTokenKind.Text => new MacroText(token.Value),
                MacroTokenKind.Wait => ParseWait(token),
                MacroTokenKind.Calc => ParseCalc(token),
                MacroTokenKind.Hotkey => ParseHotkey(token),
                _ => throw new MacroParseException("Unknown macro token", token.Position)
            });
        }

        return new MacroProgram(nodes);
    }

    private static MacroWait ParseWait(MacroToken token)
    {
        if (!long.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds) || milliseconds < 0)
            throw new MacroParseException("{wait} requires a non-negative integer number of milliseconds", token.Position);

        try
        {
            return new MacroWait(TimeSpan.FromMilliseconds(milliseconds));
        }
        catch (OverflowException)
        {
            throw new MacroParseException("{wait} duration is too large", token.Position);
        }
    }

    private static MacroCalc ParseCalc(MacroToken token)
    {
        if (string.IsNullOrWhiteSpace(token.Value))
            throw new MacroParseException("{calc} requires an expression", token.Position);
        return new MacroCalc(token.Value);
    }

    private static MacroHotkey ParseHotkey(MacroToken token)
    {
        var value = token.Value;
        if (value.Length < 2)
            throw new MacroParseException("{hk} requires a state and key", token.Position);

        var state = value[..^1].ToUpperInvariant();
        var key = value[^1];
        if (state.Any(character => character is not ('M' or 'S' or 'H')))
            throw new MacroParseException("{hk} state may contain only M, S, and H", token.Position);

        if (!char.IsAsciiLetter(key) && !HotkeyPunctuation.Contains(key))
            throw new MacroParseException("{hk} key must be a letter or one of @ ; : , . /", token.Position);

        return new MacroHotkey(state, char.IsAsciiLetter(key) ? char.ToLowerInvariant(key) : key);
    }
}
