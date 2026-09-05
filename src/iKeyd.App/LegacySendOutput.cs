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

            if (modifiers.Count == 0 && legacySendText[index] != '{')
            {
                plain.Append(legacySendText[index]);
                index++;
                continue;
            }

            FlushPlain();

            if (index >= legacySendText.Length)
            {
                _keyboard.SendText(legacySendText[modifierStart..]);
                break;
            }

            if (legacySendText[index] == '{')
            {
                var close = legacySendText.IndexOf('}', index + 1);
                if (close < 0)
                {
                    _keyboard.SendText(legacySendText[modifierStart..]);
                    break;
                }

                var token = legacySendText[(index + 1)..close];
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
        => SendChord([modifier], virtualKey);

    public void SendChord(ushort modifier1, ushort modifier2, ushort virtualKey)
        => SendChord([modifier1, modifier2], virtualKey);

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
