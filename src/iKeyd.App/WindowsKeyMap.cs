using System.Globalization;
using iKeyd.Core.Chords;
using iKeyd.Core.Input;

namespace iKeyd.App;

internal static class WindowsKeyMap
{
    public const ushort Backspace = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Enter = 0x0D;
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
    public const ushort RightWin = 0x5C;
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
            return new KeyId(((char)virtualKey).ToString());
        if (virtualKey is >= 0x30 and <= 0x39)
            return new KeyId(((char)virtualKey).ToString());
        if (virtualKey is >= F1 and <= F12)
            return new KeyId($"F{virtualKey - F1 + 1}");

        return virtualKey switch
        {
            OemSemicolon => new KeyId("SColon"),
            OemPlus => new KeyId("Colon"),
            OemComma => new KeyId("Comma"),
            OemPeriod => new KeyId("Dot"),
            OemSlash => new KeyId("Slash"),
            OemAt => new KeyId("AT"),
            _ => null
        };
    }

    public static KeyboardKey Keyboard(ushort virtualKey)
        => new(virtualKey, 0, IsExtended(virtualKey));

    public static bool TryResolveNamedKey(string name, out KeyboardKey key)
    {
        var normalized = name.Trim().ToUpperInvariant();
        if (TryResolveVirtualKeyToken(normalized, out key))
            return true;

        ushort virtualKey;
        if (normalized.Length > 1 && normalized[0] == 'F' &&
            int.TryParse(normalized[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var functionNumber) &&
            functionNumber is >= 1 and <= 12)
        {
            virtualKey = (ushort)(F1 + functionNumber - 1);
        }
        else
        {
            virtualKey = normalized switch
            {
                "UP" => Up,
                "DOWN" => Down,
                "LEFT" => Left,
                "RIGHT" => Right,
                "HOME" => Home,
                "END" => End,
                "PGUP" or "PAGEUP" => PageUp,
                "PGDN" or "PAGEDOWN" => PageDown,
                "TAB" => Tab,
                "BS" or "BACKSPACE" => Backspace,
                "DEL" or "DELETE" => Delete,
                "ENTER" or "RETURN" => Enter,
                "INS" or "INSERT" => Insert,
                "ESC" or "ESCAPE" => Escape,
                "APPSKEY" or "APPS" => Apps,
                "SPACE" => Space,
                "SHIFT" => Shift,
                "CTRL" or "CONTROL" => Control,
                "ALT" => Alt,
                "LWIN" => LeftWin,
                "RWIN" => RightWin,
                "CAPSLOCK" => CapsLock,
                "KANA" => Kana,
                "CONVERT" or "HENKAN" => Convert,
                "NONCONVERT" or "MUHENKAN" => NonConvert,
                "VOLUME_UP" or "VOLUMEUP" => VolumeUp,
                "VOLUME_DOWN" or "VOLUMEDOWN" => VolumeDown,
                "VOLUME_MUTE" or "VOLUMEMUTE" => VolumeMute,
                "MEDIA_NEXT" or "MEDIANEXT" => MediaNext,
                "MEDIA_PREV" or "MEDIA_PREVIOUS" or "MEDIAPREV" => MediaPrevious,
                "MEDIA_PLAY_PAUSE" or "MEDIAPLAYPAUSE" => MediaPlayPause,
                _ => 0
            };
        }

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

    private static bool TryResolveVirtualKeyToken(string normalized, out KeyboardKey key)
    {
        key = default;
        if (!normalized.StartsWith("VK", StringComparison.Ordinal) || normalized.Length < 4)
            return false;

        var scanIndex = normalized.IndexOf("SC", 2, StringComparison.Ordinal);
        var virtualKeyHex = scanIndex >= 0 ? normalized[2..scanIndex] : normalized[2..];
        if (!ushort.TryParse(virtualKeyHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var virtualKey) || virtualKey == 0)
            return false;

        ushort scanCode = 0;
        if (scanIndex >= 0)
        {
            var scanHex = normalized[(scanIndex + 2)..];
            if (scanHex.Length == 0 ||
                !ushort.TryParse(scanHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out scanCode))
                return false;
        }

        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey));
        return true;
    }

    private static bool IsExtended(ushort virtualKey)
        => virtualKey is Left or Right or Up or Down or Home or End or PageUp or PageDown or Insert or Delete or LeftWin or RightWin or Apps;
}
