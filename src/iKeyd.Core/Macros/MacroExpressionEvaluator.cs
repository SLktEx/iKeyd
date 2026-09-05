namespace iKeyd.Core.Macros;

public sealed class MacroExpressionEvaluator
{
    public long Evaluate(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var parser = new ExpressionParser(expression);
        var value = parser.ParseExpression();
        parser.ExpectEnd();
        return value;
    }

    private sealed class ExpressionParser(string source)
    {
        private int _position;

        public long ParseExpression() => ParseAddSubtract();

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_position != source.Length)
                throw Error($"Unexpected character '{source[_position]}'");
        }

        private long ParseAddSubtract()
        {
            var value = ParseMultiplyDivideModulo();
            while (true)
            {
                if (TryConsume('+'))
                    value = checked(value + ParseMultiplyDivideModulo());
                else if (TryConsume('-'))
                    value = checked(value - ParseMultiplyDivideModulo());
                else
                    return value;
            }
        }

        private long ParseMultiplyDivideModulo()
        {
            var value = ParsePower();
            while (true)
            {
                if (TryConsume('*'))
                    value = checked(value * ParsePower());
                else if (TryConsume('/'))
                {
                    var divisor = ParsePower();
                    if (divisor == 0)
                        throw Error("Division by zero");
                    value = checked(value / divisor);
                }
                else if (TryConsume('%'))
                {
                    var divisor = ParsePower();
                    if (divisor == 0)
                        throw Error("Modulo by zero");
                    value = value % divisor;
                }
                else
                    return value;
            }
        }

        // Legacy hotkeySKG reduces the left-most exponent first, so exponentiation is left-associative here.
        private long ParsePower()
        {
            var value = ParseUnary();
            while (TryConsume('^'))
                value = Pow(value, ParseUnary());
            return value;
        }

        private long ParseUnary()
        {
            if (TryConsume('+'))
                return ParseUnary();
            if (TryConsume('-'))
                return checked(-ParseUnary());
            return ParsePrimary();
        }

        private long ParsePrimary()
        {
            if (TryConsume('('))
            {
                var parenthesizedValue = ParseExpression();
                if (!TryConsume(')'))
                    throw Error("Missing closing parenthesis");
                return parenthesizedValue;
            }

            SkipWhitespace();
            var start = _position;
            while (_position < source.Length && char.IsAsciiDigit(source[_position]))
                _position++;
            if (start == _position)
                throw Error("Expected an integer");

            if (!long.TryParse(source.AsSpan(start, _position - start), out var parsedValue))
                throw Error("Integer is outside Int64 range");
            return parsedValue;
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (_position >= source.Length || source[_position] != expected)
                return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < source.Length && char.IsWhiteSpace(source[_position]))
                _position++;
        }

        private FormatException Error(string message)
            => new($"{message} at expression position {_position}.");

        private long Pow(long value, long exponent)
        {
            if (exponent < 0)
                throw Error("Negative exponents are not supported");

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
}
