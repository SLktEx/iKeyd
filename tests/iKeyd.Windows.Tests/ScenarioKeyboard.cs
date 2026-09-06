using iKeyd.Core.Input;

namespace iKeyd.Windows.Tests;

internal static class ScenarioKeyboard
{
    public static ushort ResolveVirtualKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9') return ch;
        }
        var normalized = key.Trim().ToUpperInvariant();
        if (normalized.Length > 1 && normalized[0] == 'F' && int.TryParse(normalized[1..], out var function) && function is >= 1 and <= 12)
            return (ushort)(0x70 + function - 1);
        return normalized switch
        {
            "BS" or "BACKSPACE" => 0x08, "TAB" => 0x09, "ENTER" or "RETURN" => 0x0D,
            "SHIFT" => 0x10, "CTRL" or "CONTROL" => 0x11, "ALT" => 0x12, "KANA" => 0x15,
            "ESC" or "ESCAPE" => 0x1B, "CONVERT" or "HENKAN" => 0x1C, "NONCONVERT" or "MUHENKAN" => 0x1D,
            "SPACE" => 0x20, "PGUP" or "PAGEUP" => 0x21, "PGDN" or "PAGEDOWN" => 0x22,
            "END" => 0x23, "HOME" => 0x24, "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "INS" or "INSERT" => 0x2D, "DEL" or "DELETE" => 0x2E, "LWIN" => 0x5B, "RWIN" => 0x5C, "APPS" or "APPSKEY" => 0x5D,
            "SCOLON" => 0xBA, "COLON" => 0xBB, "COMMA" => 0xBC, "MINUS" => 0xBD, "DOT" => 0xBE, "SLASH" => 0xBF, "AT" => 0xC0,
            _ => throw new NotSupportedException($"No Windows virtual-key mapping for scenario key '{key}'.")
        };
    }

    public static byte ResolveScanCode(string key) => key.Trim().ToUpperInvariant() switch
    {
        "KANA" => 0x70, "CONVERT" or "HENKAN" => 0x79, "NONCONVERT" or "MUHENKAN" => 0x7B,
        "COLON" => 0x28, "COMMA" => 0x33, _ => 0
    };

    public static string ResolveName(ushort virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x7B) return $"F{virtualKey - 0x70 + 1}";
        return virtualKey switch
        {
            0x08 => "BACKSPACE", 0x09 => "TAB", 0x0D => "ENTER", 0x10 or 0xA0 or 0xA1 => "SHIFT",
            0x11 or 0xA2 or 0xA3 => "CTRL", 0x12 or 0xA4 or 0xA5 => "ALT", 0x15 => "KANA", 0x1B => "ESC",
            0x1C => "CONVERT", 0x1D => "NONCONVERT", 0x20 => "SPACE", 0x21 => "PGUP", 0x22 => "PGDN",
            0x23 => "END", 0x24 => "HOME", 0x25 => "LEFT", 0x26 => "UP", 0x27 => "RIGHT", 0x28 => "DOWN",
            0x2D => "INSERT", 0x2E => "DELETE", 0x5B => "LWIN", 0x5C => "RWIN", 0x5D => "APPSKEY",
            0xBA => "SCOLON", 0xBB => "COLON", 0xBC => "COMMA", 0xBD => "MINUS", 0xBE => "DOT", 0xBF => "SLASH", 0xC0 => "AT",
            _ => $"VK{virtualKey:X2}"
        };
    }

    public static KeyboardKey Keyboard(string key)
    {
        var virtualKey = ResolveVirtualKey(key);
        return new KeyboardKey(virtualKey, ResolveScanCode(key), IsExtended(virtualKey));
    }

    public static bool IsExtended(ushort virtualKey)
        => virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D;
}
