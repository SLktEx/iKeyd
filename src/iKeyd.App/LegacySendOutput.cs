using System.Globalization;
using System.Runtime.InteropServices;
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

        if (!ContainsLegacySyntax(legacySendText))
        {
            _keyboard.SendText(legacySendText);
            return;
        }

        if (legacySendText.Length >= 3 && legacySendText[0] == '{' && legacySendText[^1] == '}' &&
            legacySendText.AsSpan(1, legacySendText.Length - 2).IndexOf('{') < 0 &&
            WindowsKeyMap.TryResolveNamedKey(legacySendText.AsSpan(1, legacySendText.Length - 2), out var directNamedKey))
        {
            _keyboard.SendKeyPress(directNamedKey);
            return;
        }

        var plain = new StringBuilder();
        void FlushPlain()
        {
            if (plain.Length == 0) return;
            _keyboard.SendText(plain.ToString());
            plain.Clear();
        }

        for (var index = 0; index < legacySendText.Length;)
        {
            var modifiers = new List<ushort>();
            var modifierStart = index;
            while (index < legacySendText.Length && TryModifier(legacySendText[index], out var modifier))
            {
                if (!modifiers.Contains(modifier)) modifiers.Add(modifier);
                index++;
            }

            if (index >= legacySendText.Length)
            {
                FlushPlain();
                throw UnsupportedSyntax(legacySendText[modifierStart..], "modifier prefix is missing a target key");
            }

            if (modifiers.Count == 0 && legacySendText[index] != '{')
            {
                plain.Append(legacySendText[index++]);
                continue;
            }

            FlushPlain();
            if (legacySendText[index] == '{')
            {
                if (index + 2 < legacySendText.Length && legacySendText[index + 1] == '}' && legacySendText[index + 2] == '}')
                {
                    if (!TrySendSpecialToken("}".AsSpan(), modifiers))
                        throw UnsupportedSyntax(legacySendText[modifierStart..(index + 3)], "unknown escaped brace token");
                    index += 3;
                    continue;
                }

                var close = legacySendText.IndexOf('}', index + 1);
                if (close < 0) throw UnsupportedSyntax(legacySendText[modifierStart..], "unterminated brace token");
                var token = legacySendText.AsSpan(index + 1, close - index - 1);
                if (WindowsKeyMap.TryResolveNamedKey(token, out var namedKey)) SendKeyWithModifiers(namedKey, modifiers);
                else if (!TrySendSpecialToken(token, modifiers)) throw UnsupportedSyntax(legacySendText[modifierStart..(close + 1)], "unknown brace token");
                index = close + 1;
                continue;
            }

            var character = legacySendText[index];
            if (!TrySendModifiedCharacter(character, modifiers))
                throw UnsupportedSyntax(legacySendText[modifierStart..(index + 1)], "modifier target cannot be mapped to a keyboard key");
            index++;
        }
        FlushPlain();
    }

    public void SendKey(ushort virtualKey) => _keyboard.SendKeyPress(WindowsKeyMap.Keyboard(virtualKey));

    public void SendChord(IReadOnlyList<ushort> modifiers, ushort virtualKey)
    {
        if (TryResolveDefaultCharacter(virtualKey, out var character) &&
            TryResolveCharacterForActiveLayout(character, out var characterKey, out var characterModifiers))
        {
            SendCharacterWithModifiers(characterKey, modifiers, characterModifiers);
            return;
        }
        SendKeyWithModifiers(WindowsKeyMap.Keyboard(virtualKey), modifiers);
    }

    public void SendChord(ushort modifier, ushort virtualKey)
    {
        if (TryResolveDefaultCharacter(virtualKey, out var character))
        {
            SendChord((IReadOnlyList<ushort>)[modifier], virtualKey);
            return;
        }

        var key = WindowsKeyMap.Keyboard(virtualKey);
        var modifierKey = WindowsKeyMap.Keyboard(modifier);
        _keyboard.SendKey(modifierKey, KeyEventKind.Down);
        try { _keyboard.SendKeyPress(key); }
        finally { _keyboard.SendKey(modifierKey, KeyEventKind.Up); }
    }

    public void SendChord(ushort modifier1, ushort modifier2, ushort virtualKey)
    {
        if (TryResolveDefaultCharacter(virtualKey, out _))
        {
            SendChord((IReadOnlyList<ushort>)[modifier1, modifier2], virtualKey);
            return;
        }

        var key = WindowsKeyMap.Keyboard(virtualKey);
        var first = WindowsKeyMap.Keyboard(modifier1);
        var second = WindowsKeyMap.Keyboard(modifier2);
        _keyboard.SendKey(first, KeyEventKind.Down);
        _keyboard.SendKey(second, KeyEventKind.Down);
        try { _keyboard.SendKeyPress(key); }
        finally
        {
            _keyboard.SendKey(second, KeyEventKind.Up);
            _keyboard.SendKey(first, KeyEventKind.Up);
        }
    }

    private void SendKeyWithModifiers(KeyboardKey key, IReadOnlyList<ushort> modifiers)
    {
        var pressed = PressTemporaryModifiers(modifiers);
        try { _keyboard.SendKeyPress(key); }
        finally { ReleaseTemporaryModifiers(pressed); }
    }

    private void SendKeyStateWithModifiers(KeyboardKey key, KeyEventKind kind, IReadOnlyList<ushort> modifiers)
    {
        var pressed = PressTemporaryModifiers(modifiers);
        try { _keyboard.SendKey(key, kind); }
        finally { ReleaseTemporaryModifiers(pressed); }
    }

    private void SendCharacterWithModifiers(KeyboardKey key, IReadOnlyList<ushort> explicitModifiers, IReadOnlyList<ushort> characterModifiers)
    {
        var explicitPressed = PressTemporaryModifiers(explicitModifiers);
        var characterPressed = PressTemporaryModifiers(characterModifiers, explicitPressed);
        try { _keyboard.SendKeyPress(key); }
        finally
        {
            ReleaseTemporaryModifiers(explicitPressed);
            ReleaseTemporaryModifiers(characterPressed);
        }
    }

    private List<ushort> PressTemporaryModifiers(IReadOnlyList<ushort> modifiers, IReadOnlyCollection<ushort>? alreadyPressed = null)
    {
        var pressed = new List<ushort>(modifiers.Count);
        foreach (var modifier in modifiers)
        {
            if (pressed.Contains(modifier) || alreadyPressed?.Contains(modifier) == true) continue;
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

    private bool TrySendSpecialToken(ReadOnlySpan<char> token, IReadOnlyList<ushort> modifiers)
    {
        var trimmed = token.Trim();
        if (TrySplitSuffix(trimmed, "down", out var stateKey) && WindowsKeyMap.TryResolveNamedKey(stateKey, out var downKey))
        { SendKeyStateWithModifiers(downKey, KeyEventKind.Down, modifiers); return true; }
        if (TrySplitSuffix(trimmed, "up", out stateKey) && WindowsKeyMap.TryResolveNamedKey(stateKey, out var upKey))
        { SendKeyStateWithModifiers(upKey, KeyEventKind.Up, modifiers); return true; }

        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace > 0 && int.TryParse(trimmed[(lastSpace + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 0 &&
            WindowsKeyMap.TryResolveNamedKey(trimmed[..lastSpace], out var repeatedKey))
        {
            for (var i = 0; i < count; i++) SendKeyWithModifiers(repeatedKey, modifiers);
            return true;
        }

        if (trimmed.Length == 1 && trimmed[0] is '{' or '}' or '!' or '#' or '^' or '+')
        {
            if (modifiers.Count == 0) _keyboard.SendText(trimmed.ToString());
            else if (!TrySendModifiedCharacter(trimmed[0], modifiers))
                throw UnsupportedSyntax($"{{{trimmed.ToString()}}}", "escaped literal cannot be mapped to a keyboard key");
            return true;
        }
        if (trimmed.StartsWith("Click,", StringComparison.OrdinalIgnoreCase)) return TrySendClick(trimmed, modifiers);
        return false;
    }

    private bool TrySendClick(ReadOnlySpan<char> token, IReadOnlyList<ushort> modifiers)
    {
        if (_desktop is null) throw UnsupportedSyntax($"{{{token.ToString()}}}", "Click token requires a desktop backend");
        var payload = token[6..].Trim();
        var controlModifier = modifiers.Count == 1 && modifiers[0] == WindowsKeyMap.Control;
        if (payload.Equals("WU", StringComparison.OrdinalIgnoreCase) || payload.Equals("WD", StringComparison.OrdinalIgnoreCase))
        {
            if (modifiers.Count != 0 && !controlModifier)
                throw UnsupportedSyntax($"{{{token.ToString()}}}", "hotkeySKG wheel Send uses only an optional Control prefix");
            _desktop.ScrollVertical(payload.Equals("WU", StringComparison.OrdinalIgnoreCase) ? 120 : -120, controlModifier);
            return true;
        }
        if (modifiers.Count != 0) throw UnsupportedSyntax($"{{{token.ToString()}}}", "coordinate Click syntax does not use keyboard modifiers in hotkeySKG");

        var parts = payload.ToString().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            throw UnsupportedSyntax($"{{{token.ToString()}}}", "Click coordinates must be integers");

        _desktop.MovePointer(new DesktopPoint(x, y));
        var button = parts.Length >= 3 ? ParseMouseButton(parts[2]) : DesktopMouseButton.Left;
        if (parts.Length < 4 || parts[3].Length == 0) { _desktop.Click(button); return true; }
        if (parts[3].Equals("Down", StringComparison.OrdinalIgnoreCase)) { _desktop.SetMouseButton(button, true); return true; }
        if (parts[3].Equals("Up", StringComparison.OrdinalIgnoreCase)) { _desktop.SetMouseButton(button, false); return true; }
        throw UnsupportedSyntax($"{{{token.ToString()}}}", "unsupported Click action");
    }

    private bool TrySendModifiedCharacter(char character, IReadOnlyList<ushort> explicitModifiers)
    {
        if (!TryResolveCharacterForActiveLayout(character, out var key, out var characterModifiers)) return false;
        SendCharacterWithModifiers(key, explicitModifiers, characterModifiers);
        return true;
    }

    private bool TryResolveCharacterForActiveLayout(char character, out KeyboardKey key, out IReadOnlyList<ushort> modifiers)
    {
        if (OperatingSystem.IsWindows())
        {
            var encoded = NativeMethods.VkKeyScanExW(character, GetTargetKeyboardLayout());
            if (encoded != -1)
            {
                var virtualKey = (ushort)(encoded & 0xff);
                var shiftState = (encoded >> 8) & 0xff;
                key = WindowsKeyMap.Keyboard(virtualKey);
                var list = new List<ushort>(3);
                if ((shiftState & 1) != 0) list.Add(WindowsKeyMap.Shift);
                if ((shiftState & 2) != 0) list.Add(WindowsKeyMap.Control);
                if ((shiftState & 4) != 0) list.Add(WindowsKeyMap.Alt);
                modifiers = list;
                return true;
            }
        }

        if (TryResolveJisCharacter(character, out key, out var shiftRequired))
        {
            modifiers = shiftRequired ? [WindowsKeyMap.Shift] : [];
            return true;
        }
        modifiers = [];
        return false;
    }

    private nint GetTargetKeyboardLayout()
    {
        var window = _desktop?.GetActiveWindow().Value ?? NativeMethods.GetForegroundWindow();
        if (window != 0)
        {
            var threadId = NativeMethods.GetWindowThreadProcessId(window, out _);
            if (threadId != 0) return NativeMethods.GetKeyboardLayout(threadId);
        }
        return NativeMethods.GetKeyboardLayout(0);
    }

    private static bool TryResolveDefaultCharacter(ushort virtualKey, out char character)
    {
        character = virtualKey switch
        {
            WindowsKeyMap.OemSemicolon => ';',
            WindowsKeyMap.OemPlus => ':',
            WindowsKeyMap.OemComma => ',',
            WindowsKeyMap.OemPeriod => '.',
            WindowsKeyMap.OemSlash => '/',
            WindowsKeyMap.OemAt => '@',
            WindowsKeyMap.OemMinus => '-',
            _ => '\0'
        };
        return character != '\0';
    }

    private static bool TryResolveJisCharacter(char character, out KeyboardKey key, out bool shiftRequired)
    {
        shiftRequired = false;
        ushort virtualKey = character switch
        {
            >= 'a' and <= 'z' => char.ToUpperInvariant(character), >= 'A' and <= 'Z' => character, >= '0' and <= '9' => character,
            ';' => WindowsKeyMap.OemSemicolon, ':' => WindowsKeyMap.OemPlus, ',' => WindowsKeyMap.OemComma, '.' => WindowsKeyMap.OemPeriod,
            '/' => WindowsKeyMap.OemSlash, '@' => WindowsKeyMap.OemAt, '-' => WindowsKeyMap.OemMinus,
            '!' => (ushort)'1', '"' => (ushort)'2', '#' => (ushort)'3', '$' => (ushort)'4', '%' => (ushort)'5', '&' => (ushort)'6',
            '\'' => (ushort)'7', '(' => (ushort)'8', ')' => (ushort)'9', '=' => WindowsKeyMap.OemMinus, '<' => WindowsKeyMap.OemComma,
            '>' => WindowsKeyMap.OemPeriod, '[' or '{' => 0xDB, '\\' or '|' => 0xDC, ']' or '}' => 0xDD, '^' or '~' => 0xDE, '_' => 0xE2,
            _ => 0
        };
        shiftRequired = character is '!' or '"' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '=' or '<' or '>' or '{' or '}' or '|' or '~' or '_';
        if (virtualKey == 0) { key = default; return false; }
        key = WindowsKeyMap.Keyboard(virtualKey);
        return true;
    }

    private static DesktopMouseButton ParseMouseButton(string value) => value.ToUpperInvariant() switch
    {
        "LEFT" => DesktopMouseButton.Left, "RIGHT" => DesktopMouseButton.Right, "MIDDLE" => DesktopMouseButton.Middle,
        _ => throw UnsupportedSyntax(value, "unsupported mouse button")
    };

    private static bool TrySplitSuffix(ReadOnlySpan<char> token, string suffix, out ReadOnlySpan<char> head)
    {
        if (token.Length > suffix.Length + 1 && token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(token[token.Length - suffix.Length - 1]))
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
        foreach (var character in value) if (character is '^' or '!' or '+' or '#' or '{') return true;
        return false;
    }

    private static bool TryModifier(char character, out ushort virtualKey)
    {
        virtualKey = character switch { '^' => WindowsKeyMap.Control, '!' => WindowsKeyMap.Alt, '+' => WindowsKeyMap.Shift, '#' => WindowsKeyMap.LeftWin, _ => 0 };
        return virtualKey != 0;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint processId);
        [DllImport("user32.dll")] public static extern nint GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern short VkKeyScanExW(char character, nint keyboardLayout);
    }
}
