using System.Globalization;
using System.Text.RegularExpressions;

namespace iKeyd.Core.Macros;

public sealed class MacroExpressionEvaluator
{
    private static readonly Regex ParenthesizedAddSubtract = new(@"\(([\d\+\-]+)\)", RegexOptions.CultureInvariant);
    private static readonly Regex Power = new(@"(\d+)(\^)(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex MultiplyDivideModulo = new(@"(\d+)([\*/%])(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex AddSubtract = new(@"(\d+)([\+\-])(\d+)", RegexOptions.CultureInvariant);

    public long Evaluate(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var reduced = ReduceLikeLegacy(expression);
        if (!long.TryParse(reduced, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Legacy macro expression did not reduce to an integer: '{reduced}'.");
        return value;
    }

    private static string ReduceLikeLegacy(string expression)
    {
        var current = expression;
        while (true)
        {
            var parentheses = ParenthesizedAddSubtract.Match(current);
            if (parentheses.Success)
            {
                var replacement = ReduceLikeLegacy(parentheses.Groups[1].Value);
                current = ReplaceMatch(current, parentheses, replacement);
                continue;
            }

            if (TryReduceBinary(current, Power, out var powered))
            {
                current = powered;
                continue;
            }

            if (TryReduceBinary(current, MultiplyDivideModulo, out var multiplied))
            {
                current = multiplied;
                continue;
            }

            if (TryReduceBinary(current, AddSubtract, out var added))
            {
                current = added;
                continue;
            }

            return current;
        }
    }

    private static bool TryReduceBinary(string source, Regex regex, out string reduced)
    {
        var match = regex.Match(source);
        if (!match.Success)
        {
            reduced = source;
            return false;
        }

        var left = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var op = match.Groups[2].Value[0];
        var right = long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var value = op switch
        {
            '+' => checked(left + right),
            '-' => checked(left - right),
            '*' => checked(left * right),
            '/' when right == 0 => throw new FormatException("Division by zero in legacy macro expression."),
            '/' => left / right,
            '%' when right == 0 => throw new FormatException("Modulo by zero in legacy macro expression."),
            '%' => left % right,
            '^' => Pow(left, right),
            _ => throw new InvalidOperationException($"Unsupported legacy macro operator '{op}'.")
        };

        reduced = ReplaceMatch(source, match, value.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static string ReplaceMatch(string source, Match match, string replacement)
        => string.Concat(source.AsSpan(0, match.Index), replacement, source.AsSpan(match.Index + match.Length));

    private static long Pow(long value, long exponent)
    {
        var result = 1L;
        var factor = value;
        var remaining = exponent;
        checked
        {
            while (remaining > 0)
            {
                if ((remaining & 1) != 0)
                    result *= factor;
                remaining >>= 1;
                if (remaining != 0)
                    factor *= factor;
            }
        }
        return result;
    }
}
