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
                modifiers.Add(modifier);
                index++;
            }

            if (index >= legacySendText.Length)
            {
                FlushPlain();
                _keyboard.SendText(legacySendText[modifierStart..]);
                break;
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
                if (legacySendText.AsSpan(index).StartsWith("{{}"))
                {
                    SendLiteral("{", modifiers);
                    index += 3;
                    continue;
                }

                if (legacySendText.AsSpan(index).StartsWith("{}}"))
                {
                    SendLiteral("}", modifiers);
                    index += 3;
                    continue;
                }

                var close = legacySendText.IndexOf('}', index + 1);
                if (close < 0)
                {
                    _keyboard.SendText(legacySendText[modifierStart..]);
                    break;
                }

                var token = legacySendText[(index + 1)..close];
                if (!TrySendBraceToken(token, modifiers))
                    _keyboard.SendText(legacySendText[modifierStart..(close + 1)]);

                index = close + 1;
                continue;
            }

            var character = legacySendText[index];
            if (WindowsKeyMap.TryResolveCharacter(character, out var characterKey))
                SendKeyWithModifiers(characterKey, modifiers);
            else
                _keyboard.SendText(legacySendText[modifierStart..(index + 1)]);
            index++;
        }

        FlushPlain();
    }

    public void SendKey(ushort virtualKey)
        => _keyboard.SendKeyPress(WindowsKeyMap.Keyboard(virtualKey));

    public void SendChord(IReadOnlyList<ushort> modifiers, ushort virtualKey)
        => SendKeyWithModifiers(WindowsKeyMap.Keyboard(virtualKey), modifiers);

    public void SendChord(ushort modifier, ushort virtualKey)
        => SendChord([modifier], virtualKey);

    public void SendChord(ushort modifier1, ushort modifier2, ushort virtualKey)
        => SendChord([modifier1, modifier2], virtualKey);

    private bool TrySendBraceToken(string token, IReadOnlyList<ushort> modifiers)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0)
            return false;

        if (TrySendClick(trimmed, modifiers))
            return true;

        if (trimmed is "!" or "#" or "^" or "+")
        {
            SendLiteral(trimmed, modifiers);
            return true;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        if (!WindowsKeyMap.TryResolveNamedKey(parts[0], out var namedKey))
            return false;

        if (parts.Length == 1)
        {
            SendKeyWithModifiers(namedKey, modifiers);
            return true;
        }

        if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 0)
        {
            for (var repeat = 0; repeat < count; repeat++)
                SendKeyWithModifiers(namedKey, modifiers);
            return true;
        }

        if (parts.Length == 2 && parts[1].Equals("DOWN", StringComparison.OrdinalIgnoreCase))
        {
            SendKeyStateWithModifiers(namedKey, KeyEventKind.Down, modifiers);
            return true;
        }

        if (parts.Length == 2 && parts[1].Equals("UP", StringComparison.OrdinalIgnoreCase))
        {
            SendKeyStateWithModifiers(namedKey, KeyEventKind.Up, modifiers);
            return true;
        }

        return false;
    }

    private bool TrySendClick(string token, IReadOnlyList<ushort> modifiers)
    {
        if (!token.StartsWith("CLICK", StringComparison.OrdinalIgnoreCase))
            return false;
        if (_desktop is null)
            return false;

        var fields = token.Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length == 1)
        {
            _desktop.Click(DesktopMouseButton.Left);
            return true;
        }

        var controlModifier = modifiers.Contains(WindowsKeyMap.Control);
        var first = fields[1];
        if (first.Equals("WU", StringComparison.OrdinalIgnoreCase) || first.Equals("WHEELUP", StringComparison.OrdinalIgnoreCase))
        {
            _desktop.ScrollVertical(120, controlModifier);
            return true;
        }
        if (first.Equals("WD", StringComparison.OrdinalIgnoreCase) || first.Equals("WHEELDOWN", StringComparison.OrdinalIgnoreCase))
        {
            _desktop.ScrollVertical(-120, controlModifier);
            return true;
        }

        var fieldIndex = 1;
        if (fields.Length >= 3 &&
            int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
            int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            _desktop.MovePointer(new DesktopPoint(x, y));
            fieldIndex = 3;
        }

        var button = DesktopMouseButton.Left;
        if (fieldIndex < fields.Length && TryParseMouseButton(fields[fieldIndex], out var parsedButton))
        {
            button = parsedButton;
            fieldIndex++;
        }

        if (fieldIndex < fields.Length && fields[fieldIndex].Equals("DOWN", StringComparison.OrdinalIgnoreCase))
            _desktop.SetMouseButton(button, true);
        else if (fieldIndex < fields.Length && fields[fieldIndex].Equals("UP", StringComparison.OrdinalIgnoreCase))
            _desktop.SetMouseButton(button, false);
        else
            _desktop.Click(button);

        return true;
    }

    private static bool TryParseMouseButton(string value, out DesktopMouseButton button)
    {
        if (value.Equals("L", StringComparison.OrdinalIgnoreCase) || value.Equals("LEFT", StringComparison.OrdinalIgnoreCase))
        {
            button = DesktopMouseButton.Left;
            return true;
        }
        if (value.Equals("R", StringComparison.OrdinalIgnoreCase) || value.Equals("RIGHT", StringComparison.OrdinalIgnoreCase))
        {
            button = DesktopMouseButton.Right;
            return true;
        }
        if (value.Equals("M", StringComparison.OrdinalIgnoreCase) || value.Equals("MIDDLE", StringComparison.OrdinalIgnoreCase))
        {
            button = DesktopMouseButton.Middle;
            return true;
        }

        button = default;
        return false;
    }

    private void SendLiteral(string text, IReadOnlyList<ushort> modifiers)
    {
        if (modifiers.Count == 0)
        {
            _keyboard.SendText(text);
            return;
        }

        if (text.Length == 1 && WindowsKeyMap.TryResolveCharacter(text[0], out var key))
            SendKeyWithModifiers(key, modifiers);
        else
            _keyboard.SendText(text);
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
