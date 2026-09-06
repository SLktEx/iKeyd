using System.Globalization;
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
    public const ushort VolumeMute = 0xAD;
    public const ushort VolumeDown = 0xAE;
    public const ushort VolumeUp = 0xAF;
    public const ushort MediaNext = 0xB0;
    public const ushort MediaPrevious = 0xB1;
    public const ushort MediaPlayPause = 0xB3;
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
            _ => (KeyId?)null
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
        if (TryResolveVirtualScanCode(normalized, out key))
            return true;

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
        else if (normalized.Equals("PGUP", StringComparison.OrdinalIgnoreCase)) virtualKey = PageUp;
        else if (normalized.Equals("PGDN", StringComparison.OrdinalIgnoreCase)) virtualKey = PageDown;
        else if (normalized.Equals("TAB", StringComparison.OrdinalIgnoreCase)) virtualKey = Tab;
        else if (normalized.Equals("BS", StringComparison.OrdinalIgnoreCase) || normalized.Equals("BACKSPACE", StringComparison.OrdinalIgnoreCase)) virtualKey = Backspace;
        else if (normalized.Equals("DEL", StringComparison.OrdinalIgnoreCase) || normalized.Equals("DELETE", StringComparison.OrdinalIgnoreCase)) virtualKey = Delete;
        else if (normalized.Equals("ENTER", StringComparison.OrdinalIgnoreCase)) virtualKey = Enter;
        else if (normalized.Equals("INS", StringComparison.OrdinalIgnoreCase) || normalized.Equals("INSERT", StringComparison.OrdinalIgnoreCase)) virtualKey = Insert;
        else if (normalized.Equals("ESC", StringComparison.OrdinalIgnoreCase) || normalized.Equals("ESCAPE", StringComparison.OrdinalIgnoreCase)) virtualKey = Escape;
        else if (normalized.Equals("APPSKEY", StringComparison.OrdinalIgnoreCase) || normalized.Equals("APPS", StringComparison.OrdinalIgnoreCase)) virtualKey = Apps;
        else if (normalized.Equals("SPACE", StringComparison.OrdinalIgnoreCase)) virtualKey = Space;
        else if (normalized.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || normalized.Equals("CONTROL", StringComparison.OrdinalIgnoreCase)) virtualKey = Control;
        else if (normalized.Equals("SHIFT", StringComparison.OrdinalIgnoreCase)) virtualKey = Shift;
        else if (normalized.Equals("ALT", StringComparison.OrdinalIgnoreCase)) virtualKey = Alt;
        else if (normalized.Equals("VOLUME_UP", StringComparison.OrdinalIgnoreCase)) virtualKey = VolumeUp;
        else if (normalized.Equals("VOLUME_DOWN", StringComparison.OrdinalIgnoreCase)) virtualKey = VolumeDown;
        else if (normalized.Equals("VOLUME_MUTE", StringComparison.OrdinalIgnoreCase)) virtualKey = VolumeMute;
        else if (normalized.Equals("MEDIA_NEXT", StringComparison.OrdinalIgnoreCase)) virtualKey = MediaNext;
        else if (normalized.Equals("MEDIA_PREV", StringComparison.OrdinalIgnoreCase)) virtualKey = MediaPrevious;
        else if (normalized.Equals("MEDIA_PLAY_PAUSE", StringComparison.OrdinalIgnoreCase)) virtualKey = MediaPlayPause;
        else
        {
            key = default;
            return false;
        }

        key = Keyboard(virtualKey);
        return true;
    }

    public static bool TryResolveCharacter(char character, out KeyboardKey key)
    {
        ushort virtualKey;
        switch (character)
        {
            case >= 'a' and <= 'z': virtualKey = char.ToUpperInvariant(character); break;
            case >= 'A' and <= 'Z': virtualKey = character; break;
            case >= '0' and <= '9': virtualKey = character; break;
            case ' ': virtualKey = Space; break;
            case ';': virtualKey = OemSemicolon; break;
            case ':': virtualKey = OemPlus; break;
            case ',': virtualKey = OemComma; break;
            case '.': virtualKey = OemPeriod; break;
            case '/': virtualKey = OemSlash; break;
            case '@': virtualKey = OemAt; break;
            case '-': virtualKey = OemMinus; break;
            default:
                key = default;
                return false;
        }

        key = Keyboard(virtualKey);
        return true;
    }

    private static bool TryResolveVirtualScanCode(ReadOnlySpan<char> name, out KeyboardKey key)
    {
        var normalized = name.Trim();
        if (normalized.Length < 4 ||
            (normalized[0] is not ('v' or 'V')) ||
            (normalized[1] is not ('k' or 'K')))
        {
            key = default;
            return false;
        }

        var scanMarker = normalized.IndexOf("sc", StringComparison.OrdinalIgnoreCase);
        if (scanMarker < 3 || scanMarker + 2 >= normalized.Length ||
            !ushort.TryParse(normalized[2..scanMarker], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var virtualKey) ||
            !ushort.TryParse(normalized[(scanMarker + 2)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scanCode))
        {
            key = default;
            return false;
        }

        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey), PreserveVirtualKeyWithScanCode: true);
        return true;
    }

    private static bool IsExtended(ushort virtualKey)
        => virtualKey is PageUp or PageDown or End or Home or Left or Up or Right or Down or Insert or Delete or LeftWin or Apps;
}
