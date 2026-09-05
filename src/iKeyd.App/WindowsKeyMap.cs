using iKeyd.Core.Chords;
using iKeyd.Core.Input;

namespace iKeyd.App;

internal static class WindowsKeyMap
{
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Alt = 0x12;
    public const ushort Pause = 0x13;
    public const ushort CapsLock = 0x14;
    public const ushort Kana = 0x15;
    public const ushort Escape = 0x1B;
    public const ushort Convert = 0x1C;
    public const ushort NonConvert = 0x1D;
    public const ushort Space = 0x20;
    public const ushort PageUp = 0x21;
    public const ushort PageDown = 0x22;
    public const ushort End = 0x23;
    public const ushort Home = 0x24;
    public const ushort Left = 0x25;
    public const ushort Up = 0x26;
    public const ushort Right = 0x27;
    public const ushort Down = 0x28;
    public const ushort Insert = 0x2D;
    public const ushort Delete = 0x2E;
    public const ushort LeftWin = 0x5B;
    public const ushort Apps = 0x5D;
    public const ushort Numpad0 = 0x60;
    public const ushort F1 = 0x70;
    public const ushort F12 = 0x7B;
    public const ushort Backspace = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Enter = 0x0D;
    public const ushort OemSemicolon = 0xBA;
    public const ushort OemPlus = 0xBB;
    public const ushort OemComma = 0xBC;
    public const ushort OemMinus = 0xBD;
    public const ushort OemPeriod = 0xBE;
    public const ushort OemSlash = 0xBF;
    public const ushort OemAt = 0xC0;

    public static KeyId? TryResolveKeyId(ushort virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
            return new KeyId((KeyCode)((int)KeyCode.A + virtualKey - 0x41));
        if (virtualKey is >= 0x30 and <= 0x39)
            return new KeyId((KeyCode)((int)KeyCode.Digit0 + virtualKey - 0x30));
        if (virtualKey is >= F1 and <= F12)
            return new KeyId((KeyCode)((int)KeyCode.F1 + virtualKey - F1));

        return virtualKey switch
        {
            OemSemicolon => new KeyId(KeyCode.SColon),
            OemPlus => new KeyId(KeyCode.Colon),
            OemComma => new KeyId(KeyCode.Comma),
            OemPeriod => new KeyId(KeyCode.Dot),
            OemSlash => new KeyId(KeyCode.Slash),
            OemAt => new KeyId(KeyCode.At),
            _ => null
        };
    }

    public static KeyboardKey Keyboard(ushort virtualKey)
        => new(virtualKey, 0, IsExtended(virtualKey));

    public static bool TryResolveNamedKey(string name, out KeyboardKey key)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TryResolveNamedKey(name.AsSpan(), out key);
    }

    public static bool TryResolveNamedKey(ReadOnlySpan<char> name, out KeyboardKey key)
    {
        var normalized = name.Trim();
        ushort virtualKey;

        if (normalized.Length > 1 && (normalized[0] is 'F' or 'f') &&
            int.TryParse(normalized[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 12)
        {
            virtualKey = (ushort)(F1 + functionNumber - 1);
        }
        else if (normalized.Equals("UP", StringComparison.OrdinalIgnoreCase)) virtualKey = Up;
        else if (normalized.Equals("DOWN", StringComparison.OrdinalIgnoreCase)) virtualKey = Down;
        else if (normalized.Equals("LEFT", StringComparison.OrdinalIgnoreCase)) virtualKey = Left;
        else if (normalized.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)) virtualKey = Right;
        else if (normalized.Equals("HOME", StringComparison.OrdinalIgnoreCase)) virtualKey = Home;
        else if (normalized.Equals("END", StringComparison.OrdinalIgnoreCase)) virtualKey = End;
        else if (normalized.Equals("PGUP", StringComparison.OrdinalIgnoreCase) || normalized.Equals("PAGEUP", StringComparison.OrdinalIgnoreCase)) virtualKey = PageUp;
        else if (normalized.Equals("PGDN", StringComparison.OrdinalIgnoreCase) || normalized.Equals("PAGEDOWN", StringComparison.OrdinalIgnoreCase)) virtualKey = PageDown;
        else if (normalized.Equals("TAB", StringComparison.OrdinalIgnoreCase)) virtualKey = Tab;
        else if (normalized.Equals("BS", StringComparison.OrdinalIgnoreCase) || normalized.Equals("BACKSPACE", StringComparison.OrdinalIgnoreCase)) virtualKey = Backspace;
        else if (normalized.Equals("DEL", StringComparison.OrdinalIgnoreCase) || normalized.Equals("DELETE", StringComparison.OrdinalIgnoreCase)) virtualKey = Delete;
        else if (normalized.Equals("ENTER", StringComparison.OrdinalIgnoreCase) || normalized.Equals("RETURN", StringComparison.OrdinalIgnoreCase)) virtualKey = Enter;
        else if (normalized.Equals("INS", StringComparison.OrdinalIgnoreCase) || normalized.Equals("INSERT", StringComparison.OrdinalIgnoreCase)) virtualKey = Insert;
        else if (normalized.Equals("ESC", StringComparison.OrdinalIgnoreCase) || normalized.Equals("ESCAPE", StringComparison.OrdinalIgnoreCase)) virtualKey = Escape;
        else if (normalized.Equals("APPSKEY", StringComparison.OrdinalIgnoreCase) || normalized.Equals("APPS", StringComparison.OrdinalIgnoreCase)) virtualKey = Apps;
        else if (normalized.Equals("SPACE", StringComparison.OrdinalIgnoreCase)) virtualKey = Space;
        else virtualKey = 0;

        if (virtualKey == 0)
        {
            key = default;
            return false;
        }

        key = Keyboard(virtualKey);
        return true;
    }

    public static bool TryResolveCharacter(char character, out KeyboardKey key)
    {
        var upper = char.ToUpperInvariant(character);
        ushort virtualKey = upper switch
        {
            >= 'A' and <= 'Z' => upper,
            >= '0' and <= '9' => upper,
            ';' or ':' => OemSemicolon,
            ',' => OemComma,
            '.' => OemPeriod,
            '/' => OemSlash,
            '@' => OemAt,
            '-' => OemMinus,
            _ => 0
        };

        if (virtualKey == 0)
        {
            key = default;
            return false;
        }

        key = Keyboard(virtualKey);
        return true;
    }

    private static bool IsExtended(ushort virtualKey)
        => virtualKey is Left or Right or Up or Down or Home or End or PageUp or PageDown or Insert or Delete or LeftWin or Apps;
}
