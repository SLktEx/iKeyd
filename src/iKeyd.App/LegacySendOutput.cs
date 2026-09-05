using System.Text;
using iKeyd.Core.Input;
using iKeyd.Core.Macros;

namespace iKeyd.App;

internal sealed class LegacySendOutput : IMacroOutput
{
    private readonly IKeyboardOutput _keyboard;

    public LegacySendOutput(IKeyboardOutput keyboard)
        => _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));

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
                var close = legacySendText.IndexOf('}', index + 1);
                if (close < 0)
                {
                    _keyboard.SendText(legacySendText[modifierStart..]);
                    break;
                }

                var token = legacySendText.AsSpan(index + 1, close - index - 1);
                if (WindowsKeyMap.TryResolveNamedKey(token, out var namedKey))
                    SendKeyWithModifiers(namedKey, modifiers);
                else
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
