using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using iKeyd.Core.Macros;

namespace iKeyd.App;

internal sealed class LegacySendOutput : IMacroOutput
{
    private readonly object _sendGate = new();
    private readonly IKeyboardOutput _keyboard;
    private readonly IDesktopBackend? _desktop;
    private readonly HashSet<ushort> _heldModifiers = [];

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
        lock (_sendGate)
            SendCore(legacySendText);
    }

    public void SendWithHeldModifier(ushort modifier, string legacySendText)
    {
        ArgumentNullException.ThrowIfNull(legacySendText);
        lock (_sendGate)
        {
            var modifierKey = WindowsKeyMap.Keyboard(modifier);
            var wasAlreadyHeld = _heldModifiers.Contains(modifier);
            if (!wasAlreadyHeld)
            {
                _keyboard.SendKey(modifierKey, KeyEventKind.Down);
                _heldModifiers.Add(modifier);
            }

            try
            {
                SendCore(legacySendText);
            }
            finally
            {
                if (!wasAlreadyHeld)
                {
                    _keyboard.SendKey(modifierKey, KeyEventKind.Up);
                    _heldModifiers.Remove(modifier);
                }
            }
        }
    }

    public void SendKey(ushort virtualKey)
    {
        lock (_sendGate)
            _keyboard.SendKeyPress(WindowsKeyMap.Keyboard(virtualKey));
    }

    public void SendChord(IReadOnlyList<ushort> modifiers, ushort virtualKey)
    {
        lock (_sendGate)
            SendKeyWithModifiers(WindowsKeyMap.Keyboard(virtualKey), modifiers);
    }

    public void SendChord(ushort modifier, ushort virtualKey)
        => SendChord([modifier], virtualKey);

    public void SendChord(ushort modifier1, ushort modifier2, ushort virtualKey)
        => SendChord([modifier1, modifier2], virtualKey);

    private void SendCore(string legacySendText)
    {
        if (legacySendText.Length == 0)
            return;

        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0)
                return;
            SendPlain(plain.ToString());
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
                SendPlain(legacySendText[modifierStart..]);
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
                    SendPlain(legacySendText[modifierStart..]);
                    break;
                }

                var token = legacySendText[(index + 1)..close];
                if (!TrySendBraceToken(token, modifiers))
                    SendPlain(legacySendText[modifierStart..(close + 1)]);

                index = close + 1;
                continue;
            }

            var character = legacySendText[index];
            if (TryTranslateCharacter(character, out var characterKey, out var characterModifiers))
                SendCharacterWithModifiers(characterKey, modifiers, characterModifiers);
            else if (WindowsKeyMap.TryResolveCharacter(character, out characterKey))
                SendKeyWithModifiers(characterKey, modifiers);
            else
                SendPlain(legacySendText[modifierStart..(index + 1)]);
            index++;
        }

        FlushPlain();
    }

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
            TrackExplicitModifier(namedKey.VirtualKey, down: true, modifiers.Count);
            return true;
        }

        if (parts.Length == 2 && parts[1].Equals("UP", StringComparison.OrdinalIgnoreCase))
        {
            SendKeyStateWithModifiers(namedKey, KeyEventKind.Up, modifiers);
            TrackExplicitModifier(namedKey.VirtualKey, down: false, modifiers.Count);
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

        var controlModifier = modifiers.Contains(WindowsKeyMap.Control) || _heldModifiers.Contains(WindowsKeyMap.Control);
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
            SendPlain(text);
            return;
        }

        if (text.Length == 1 && TryTranslateCharacter(text[0], out var translatedKey, out var characterModifiers))
            SendCharacterWithModifiers(translatedKey, modifiers, characterModifiers);
        else if (text.Length == 1 && WindowsKeyMap.TryResolveCharacter(text[0], out var key))
            SendKeyWithModifiers(key, modifiers);
        else
            SendPlain(text);
    }

    private void SendPlain(string text)
    {
        if (text.Length == 0)
            return;

        if (_heldModifiers.Count == 0)
        {
            _keyboard.SendText(text);
            return;
        }

        foreach (var character in text)
        {
            if (!TryTranslateCharacter(character, out var key, out var characterModifiers))
            {
                _keyboard.SendText(character.ToString());
                continue;
            }

            SendKeyWithModifiers(key, characterModifiers);
        }
    }

    private bool TryTranslateCharacter(char character, out KeyboardKey key, out IReadOnlyList<ushort> modifiers)
    {
        var layout = GetTargetKeyboardLayout();
        var encoded = NativeMethods.VkKeyScanExW(character, layout);
        if (encoded == -1)
        {
            key = default;
            modifiers = [];
            return false;
        }

        var virtualKey = (ushort)(encoded & 0xff);
        var shiftState = (encoded >> 8) & 0xff;
        key = WindowsKeyMap.Keyboard(virtualKey);

        var list = new List<ushort>(3);
        if ((shiftState & 1) != 0)
            list.Add(WindowsKeyMap.Shift);
        if ((shiftState & 2) != 0)
            list.Add(WindowsKeyMap.Control);
        if ((shiftState & 4) != 0)
            list.Add(WindowsKeyMap.Alt);
        modifiers = list;
        return true;
    }

    private nint GetTargetKeyboardLayout()
    {
        var window = _desktop?.GetActiveWindow().Value ?? NativeMethods.GetForegroundWindow();
        if (window != 0)
        {
            var threadId = NativeMethods.GetWindowThreadProcessId(window, out _);
            if (threadId != 0)
                return NativeMethods.GetKeyboardLayout(threadId);
        }

        return NativeMethods.GetKeyboardLayout(0);
    }

    private void SendKeyStateWithModifiers(KeyboardKey key, KeyEventKind kind, IReadOnlyList<ushort> modifiers)
    {
        var temporaryModifiers = PressTemporaryModifiers(modifiers);
        try
        {
            _keyboard.SendKey(key, kind);
        }
        finally
        {
            ReleaseTemporaryModifiers(temporaryModifiers);
        }
    }

    private void SendKeyWithModifiers(KeyboardKey key, IReadOnlyList<ushort> modifiers)
    {
        var temporaryModifiers = PressTemporaryModifiers(modifiers);
        try
        {
            _keyboard.SendKeyPress(key);
        }
        finally
        {
            ReleaseTemporaryModifiers(temporaryModifiers);
        }
    }

    private void SendCharacterWithModifiers(
        KeyboardKey key,
        IReadOnlyList<ushort> explicitModifiers,
        IReadOnlyList<ushort> characterModifiers)
    {
        var explicitPressed = PressTemporaryModifiers(explicitModifiers);
        var characterPressed = PressTemporaryModifiers(characterModifiers, explicitPressed);
        try
        {
            _keyboard.SendKeyPress(key);
        }
        finally
        {
            // AHK v1 releases prefix modifiers before modifiers implicitly needed
            // to type the character. For example `^:` is observed as
            // Ctrl down, Shift down, key, Ctrl up, Shift up.
            ReleaseTemporaryModifiers(explicitPressed);
            ReleaseTemporaryModifiers(characterPressed);
        }
    }

    private List<ushort> PressTemporaryModifiers(
        IReadOnlyList<ushort> modifiers,
        IReadOnlyCollection<ushort>? alreadyPressed = null)
    {
        var pressed = new List<ushort>(modifiers.Count);
        foreach (var modifier in modifiers)
        {
            if (_heldModifiers.Contains(modifier) ||
                pressed.Contains(modifier) ||
                alreadyPressed?.Contains(modifier) == true)
                continue;
            _keyboard.SendKey(WindowsKeyMap.Keyboard(modifier), KeyEventKind.Down);
            pressed.Add(modifier);
        }
        return pressed;
    }

    private void ReleaseTemporaryModifiers(IReadOnlyList<ushort> modifiers)
    {
        for (var index = modifiers.Count - 1; index >= 0; index--)
            _keyboard.SendKey(WindowsKeyMap.Keyboard(modifiers[index]), KeyEventKind.Up);
    }

    private void TrackExplicitModifier(ushort virtualKey, bool down, int prefixModifierCount)
    {
        if (prefixModifierCount != 0 || !IsModifier(virtualKey))
            return;
        if (down)
            _heldModifiers.Add(virtualKey);
        else
            _heldModifiers.Remove(virtualKey);
    }

    private static bool IsModifier(ushort virtualKey)
        => virtualKey is WindowsKeyMap.Control or WindowsKeyMap.Alt or WindowsKeyMap.Shift or WindowsKeyMap.LeftWin or WindowsKeyMap.RightWin;

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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll")]
        public static extern nint GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanExW(char character, nint keyboardLayout);
    }
}
