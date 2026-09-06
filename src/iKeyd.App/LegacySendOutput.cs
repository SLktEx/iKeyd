using System.Globalization;
using System.Text;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Macros;

namespace iKeyd.App;

internal sealed class LegacySendOutput : IMacroOutput
{
    private readonly IKeyboardOutput _keyboard;
    private readonly IDesktopBackend? _desktop;

    public LegacySendOutput(IKeyboardOutput keyboard, IDesktopBackend? desktop = null)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _desktop = desktop;
    }

    public ValueTask SendAsync(string legacySendText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Send(legacySendText);
        return ValueTask.CompletedTask;
    }

    public void Send(string legacySendText)
    {
        ArgumentNullException.ThrowIfNull(legacySendText);
        if (legacySendText.Length == 0)
            return;

        // The compiled keymaps overwhelmingly emit literal text. Avoid constructing
        // parser state for that normal path and forward the existing string directly.
        if (!ContainsLegacySyntax(legacySendText))
        {
            _keyboard.SendText(legacySendText);
            return;
        }

        // Function/named-key mappings such as {F1} are also common enough to keep
        // allocation-free. Resolve the token directly from a span.
        if (legacySendText.Length >= 3 &&
            legacySendText[0] == '{' &&
            legacySendText[^1] == '}' &&
            legacySendText.AsSpan(1, legacySendText.Length - 2).IndexOf('{') < 0 &&
            WindowsKeyMap.TryResolveNamedKey(legacySendText.AsSpan(1, legacySendText.Length - 2), out var directNamedKey))
        {
            _keyboard.SendKeyPress(directNamedKey);
            return;
        }

        // General legacy macro syntax remains supported. This path is not used by
        // ordinary compiled literal mappings and can favor compatibility/readability.
        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0)
                return;
            _keyboard.SendText(plain.ToString());
            plain.Clear();
        }

        for (var index = 0; index < legacySendText.Length;)
        {
            var modifiers = new List<ushort>();
            var modifierStart = index;
            while (index < legacySendText.Length && TryModifier(legacySendText[index], out var modifier))
            {
                if (!modifiers.Contains(modifier))
                    modifiers.Add(modifier);
                index++;
            }

            if (index >= legacySendText.Length)
            {
                FlushPlain();
                throw UnsupportedSyntax(legacySendText[modifierStart..], "modifier prefix is missing a target key");
            }

            if (modifiers.Count == 0 && legacySendText[index] != '{')
            {
                plain.Append(legacySendText[index]);
                index++;
                continue;
            }

            FlushPlain();

            if (legacySendText[index] == '{')
            {
                var close = legacySendText.IndexOf('}', index + 1);
                if (close < 0)
                    throw UnsupportedSyntax(legacySendText[modifierStart..], "unterminated brace token");

                var token = legacySendText.AsSpan(index + 1, close - index - 1);
                if (WindowsKeyMap.TryResolveNamedKey(token, out var namedKey))
                    SendKeyWithModifiers(namedKey, modifiers);
                else if (!TrySendSpecialToken(token, modifiers))
                    throw UnsupportedSyntax(legacySendText[modifierStart..(close + 1)], "unknown brace token");

                index = close + 1;
                continue;
            }

            var character = legacySendText[index];
            if (!TrySendModifiedCharacter(character, modifiers))
                throw UnsupportedSyntax(legacySendText[modifierStart..(index + 1)], "modifier target cannot be mapped to a JIS keyboard key");
            index++;
        }

        FlushPlain();
    }

    public void SendKey(ushort virtualKey)
        => _keyboard.SendKeyPress(WindowsKeyMap.Keyboard(virtualKey));

    public void SendChord(IReadOnlyList<ushort> modifiers, ushort virtualKey)
        => SendKeyWithModifiers(WindowsKeyMap.Keyboard(virtualKey), modifiers);

    public void SendChord(ushort modifier, ushort virtualKey)
    {
        var key = WindowsKeyMap.Keyboard(virtualKey);
        _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier), KeyEventKind.Down);
        try
        {
            _keyboard.SendKeyPress(key);
        }
        finally
        {
            _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier), KeyEventKind.Up);
        }
    }

    public void SendChord(ushort modifier1, ushort modifier2, ushort virtualKey)
    {
        var key = WindowsKeyMap.Keyboard(virtualKey);
        _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier1), KeyEventKind.Down);
        _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier2), KeyEventKind.Down);
        try
        {
            _keyboard.SendKeyPress(key);
        }
        finally
        {
            _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier2), KeyEventKind.Up);
            _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier1), KeyEventKind.Up);
        }
    }

    private void SendKeyWithModifiers(KeyboardKey key, IReadOnlyList<ushort> modifiers)
    {
        foreach (var modifier in modifiers)
            _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier), KeyEventKind.Down);

        try
        {
            _keyboard.SendKeyPress(key);
        }
        finally
        {
            for (var index = modifiers.Count - 1; index >= 0; index--)
                _keyboard.SendKey(WindowsKeyMap.Keyboard(modifiers[index]), KeyEventKind.Up);
        }
    }

    private void SendKeyStateWithModifiers(KeyboardKey key, KeyEventKind kind, IReadOnlyList<ushort> modifiers)
    {
        foreach (var modifier in modifiers)
            _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier), KeyEventKind.Down);

        try
        {
            _keyboard.SendKey(key, kind);
        }
        finally
        {
            for (var index = modifiers.Count - 1; index >= 0; index--)
                _keyboard.SendKey(WindowsKeyMap.Keyboard(modifiers[index]), KeyEventKind.Up);
        }
    }

    private bool TrySendSpecialToken(ReadOnlySpan<char> token, IReadOnlyList<ushort> modifiers)
    {
        var trimmed = token.Trim();

        if (TrySplitSuffix(trimmed, "down", out var stateKey) &&
            WindowsKeyMap.TryResolveNamedKey(stateKey, out var downKey))
        {
            SendKeyStateWithModifiers(downKey, KeyEventKind.Down, modifiers);
            return true;
        }

        if (TrySplitSuffix(trimmed, "up", out stateKey) &&
            WindowsKeyMap.TryResolveNamedKey(stateKey, out var upKey))
        {
            SendKeyStateWithModifiers(upKey, KeyEventKind.Up, modifiers);
            return true;
        }

        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace > 0 &&
            int.TryParse(trimmed[(lastSpace + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
            count >= 0 &&
            WindowsKeyMap.TryResolveNamedKey(trimmed[..lastSpace], out var repeatedKey))
        {
            for (var i = 0; i < count; i++)
                SendKeyWithModifiers(repeatedKey, modifiers);
            return true;
        }

        if (trimmed.Length == 1 && trimmed[0] is '{' or '}' or '!' or '#' or '^' or '+')
        {
            if (modifiers.Count == 0)
                _keyboard.SendText(trimmed.ToString());
            else if (!TrySendModifiedCharacter(trimmed[0], modifiers))
                throw UnsupportedSyntax($"{{{trimmed.ToString()}}}", "escaped literal cannot be mapped to a JIS keyboard key");
            return true;
        }

        if (trimmed.StartsWith("Click,", StringComparison.OrdinalIgnoreCase))
            return TrySendClick(trimmed, modifiers);

        return false;
    }

    private bool TrySendClick(ReadOnlySpan<char> token, IReadOnlyList<ushort> modifiers)
    {
        if (_desktop is null)
            throw UnsupportedSyntax($"{{{token.ToString()}}}", "Click token requires a desktop backend");

        var payload = token[6..].Trim();
        var controlModifier = modifiers.Count == 1 && modifiers[0] == WindowsKeyMap.Control;
        if (payload.Equals("WU", StringComparison.OrdinalIgnoreCase) || payload.Equals("WD", StringComparison.OrdinalIgnoreCase))
        {
            if (modifiers.Count != 0 && !controlModifier)
                throw UnsupportedSyntax($"{{{token.ToString()}}}", "hotkeySKG wheel Send uses only an optional Control prefix");

            _desktop.ScrollVertical(payload.Equals("WU", StringComparison.OrdinalIgnoreCase) ? 120 : -120, controlModifier);
            return true;
        }

        if (modifiers.Count != 0)
            throw UnsupportedSyntax($"{{{token.ToString()}}}", "coordinate Click syntax does not use keyboard modifiers in hotkeySKG");

        var parts = payload.ToString().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            throw UnsupportedSyntax($"{{{token.ToString()}}}", "Click coordinates must be integers");
        }

        _desktop.MovePointer(new DesktopPoint(x, y));
        var button = parts.Length >= 3 ? ParseMouseButton(parts[2]) : DesktopMouseButton.Left;
        if (parts.Length < 4 || parts[3].Length == 0)
        {
            _desktop.Click(button);
            return true;
        }

        if (parts[3].Equals("Down", StringComparison.OrdinalIgnoreCase))
        {
            _desktop.SetMouseButton(button, true);
            return true;
        }
        if (parts[3].Equals("Up", StringComparison.OrdinalIgnoreCase))
        {
            _desktop.SetMouseButton(button, false);
            return true;
        }

        throw UnsupportedSyntax($"{{{token.ToString()}}}", "unsupported Click action");
    }

    private bool TrySendModifiedCharacter(char character, IReadOnlyList<ushort> explicitModifiers)
    {
        if (WindowsKeyMap.TryResolveCharacter(character, out var directKey) && !RequiresJisShift(character))
        {
            SendKeyWithModifiers(directKey, explicitModifiers);
            return true;
        }

        if (!TryResolveJisCharacter(character, out var key, out var shiftRequired))
            return false;

        if (!shiftRequired || explicitModifiers.Contains(WindowsKeyMap.Shift))
        {
            SendKeyWithModifiers(key, explicitModifiers);
            return true;
        }

        var modifiers = new List<ushort>(explicitModifiers.Count + 1);
        modifiers.AddRange(explicitModifiers);
        modifiers.Add(WindowsKeyMap.Shift);
        SendKeyWithModifiers(key, modifiers);
        return true;
    }

    private static bool TryResolveJisCharacter(char character, out KeyboardKey key, out bool shiftRequired)
    {
        shiftRequired = false;
        ushort virtualKey = character switch
        {
            >= 'a' and <= 'z' => char.ToUpperInvariant(character),
            >= 'A' and <= 'Z' => character,
            >= '0' and <= '9' => character,
            ';' => WindowsKeyMap.OemSemicolon,
            ':' => WindowsKeyMap.OemPlus,
            ',' => WindowsKeyMap.OemComma,
            '.' => WindowsKeyMap.OemPeriod,
            '/' => WindowsKeyMap.OemSlash,
            '@' => WindowsKeyMap.OemAt,
            '-' => WindowsKeyMap.OemMinus,
            '!' => (ushort)'1',
            '"' => (ushort)'2',
            '#' => (ushort)'3',
            '$' => (ushort)'4',
            '%' => (ushort)'5',
            '&' => (ushort)'6',
            '\'' => (ushort)'7',
            '(' => (ushort)'8',
            ')' => (ushort)'9',
            '=' => WindowsKeyMap.OemMinus,
            '<' => WindowsKeyMap.OemComma,
            '>' => WindowsKeyMap.OemPeriod,
            '[' or '{' => 0xDB,
            '\\' or '|' => 0xDC,
            ']' or '}' => 0xDD,
            '^' or '~' => 0xDE,
            '_' => 0xE2,
            _ => 0
        };

        shiftRequired = character is '!' or '"' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '=' or '<' or '>' or '{' or '}' or '|' or '~' or '_';
        if (virtualKey == 0)
        {
            key = default;
            return false;
        }

        key = WindowsKeyMap.Keyboard(virtualKey);
        return true;
    }

    private static bool RequiresJisShift(char character)
        => character is '!' or '"' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '=' or '<' or '>' or '{' or '}' or '|' or '~' or '_';

    private static DesktopMouseButton ParseMouseButton(string value)
        => value.ToUpperInvariant() switch
        {
            "LEFT" => DesktopMouseButton.Left,
            "RIGHT" => DesktopMouseButton.Right,
            "MIDDLE" => DesktopMouseButton.Middle,
            _ => throw UnsupportedSyntax(value, "unsupported mouse button")
        };

    private static bool TrySplitSuffix(ReadOnlySpan<char> token, string suffix, out ReadOnlySpan<char> head)
    {
        if (token.Length > suffix.Length + 1 &&
            token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            char.IsWhiteSpace(token[token.Length - suffix.Length - 1]))
        {
            head = token[..(token.Length - suffix.Length - 1)].Trim();
            return head.Length != 0;
        }

        head = default;
        return false;
    }

    private static InvalidDataException UnsupportedSyntax(string syntax, string reason)
        => new($"Unsupported hotkeySKG legacy Send syntax '{syntax}': {reason}.");

    private static bool ContainsLegacySyntax(string value)
    {
        foreach (var character in value)
        {
            if (character is '^' or '!' or '+' or '#' or '{')
                return true;
        }
        return false;
    }

    private static bool TryModifier(char character, out ushort virtualKey)
    {
        virtualKey = character switch
        {
            '^' => WindowsKeyMap.Control,
            '!' => WindowsKeyMap.Alt,
            '+' => WindowsKeyMap.Shift,
            '#' => WindowsKeyMap.LeftWin,
            _ => 0
        };
        return virtualKey != 0;
    }
}
