using System.Text;

namespace iKeyd.Core.Macros;

public sealed class MacroLexer
{
    public IReadOnlyList<MacroToken> Lex(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tokens = new List<MacroToken>();
        var text = new StringBuilder();
        var textStart = 0;

        void FlushText()
        {
            if (text.Length == 0)
                return;
            tokens.Add(new MacroToken(MacroTokenKind.Text, text.ToString(), textStart));
            text.Clear();
        }

        for (var index = 0; index < source.Length;)
        {
            if (source[index] == '{')
            {
                var close = source.IndexOf('}', index + 1);
                if (close >= 0)
                {
                    var content = source[(index + 1)..close];
                    if (TryReadInstruction(content, out var kind, out var value))
                    {
                        FlushText();
                        tokens.Add(new MacroToken(kind, value, index));
                        index = close + 1;
                        textStart = index;
                        continue;
                    }
                }
            }

            if (text.Length == 0)
                textStart = index;
            text.Append(source[index]);
            index++;
        }

        FlushText();
        return tokens;
    }

    private static bool TryReadInstruction(string content, out MacroTokenKind kind, out string value)
    {
        var trimmed = content.Trim();
        if (TryPayload(trimmed, "wait", out value))
        {
            kind = MacroTokenKind.Wait;
            return true;
        }
        if (TryPayload(trimmed, "calc", out value))
        {
            kind = MacroTokenKind.Calc;
            return true;
        }
        if (TryPayload(trimmed, "hk", out value))
        {
            kind = MacroTokenKind.Hotkey;
            return true;
        }

        kind = MacroTokenKind.Text;
        value = string.Empty;
        return false;
    }

    private static bool TryPayload(string content, string instruction, out string payload)
    {
        if (content.Length <= instruction.Length ||
            !content.StartsWith(instruction, StringComparison.OrdinalIgnoreCase) ||
            !char.IsWhiteSpace(content[instruction.Length]))
        {
            payload = string.Empty;
            return false;
        }

        payload = content[(instruction.Length + 1)..].Trim();
        return true;
    }
}
