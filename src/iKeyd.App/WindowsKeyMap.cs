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
    public const ushort PrintScreen = 0x2C;
    public const ushort Insert = 0x2D;
    public const ushort Delete = 0x2E;
    public const ushort LeftWin = 0x5B;
    public const ushort RightWin = 0x5C;
    public const ushort Apps = 0x5D;
    public const ushort Numpad0 = 0x60;
    public const ushort Numpad1 = 0x61;
    public const ushort Numpad2 = 0x62;
    public const ushort Numpad3 = 0x63;
    public const ushort Numpad4 = 0x64;
    public const ushort Numpad5 = 0x65;
    public const ushort Numpad6 = 0x66;
    public const ushort Numpad7 = 0x67;
    public const ushort Numpad8 = 0x68;
    public const ushort Numpad9 = 0x69;
    public const ushort NumpadAsterisk = 0x6A;
    public const ushort NumpadPlus = 0x6B;
    public const ushort NumpadComma = 0x6C;
    public const ushort NumpadMinus = 0x6D;
    public const ushort NumpadDot = 0x6E;
    public const ushort NumpadSlash = 0x6F;
    public const ushort F1 = 0x70;
    public const ushort F12 = 0x7B;
    public const ushort NumLock = 0x90;
    public const ushort ScrollLock = 0x91;
    public const ushort LeftShift = 0xA0;
    public const ushort RightShift = 0xA1;
    public const ushort LeftControl = 0xA2;
    public const ushort RightControl = 0xA3;
    public const ushort LeftAlt = 0xA4;
    public const ushort RightAlt = 0xA5;
    public const ushort OemSemicolon = 0xBA;
    public const ushort OemPlus = 0xBB;
    public const ushort OemComma = 0xBC;
    public const ushort OemMinus = 0xBD;
    public const ushort OemPeriod = 0xBE;
    public const ushort OemSlash = 0xBF;
    public const ushort OemAt = 0xC0;
    public const ushort OemLeftBracket = 0xDB;
    public const ushort OemYen = 0xDC;
    public const ushort OemRightBracket = 0xDD;
    public const ushort OemCaret = 0xDE;
    public const ushort Oem102 = 0xE2;

    // Japanese Windows commonly exposes 半角/全角 through an OEM/DBE slot.
    // Physical scan code 0x29 is preferred when available.
    public const ushort ZenkakuHankaku = 0xF3;

    public static KeyId? TryResolveKeyId(KeyboardKey key)
    {
        // Pause/PrintScreen have unusual scan-code sequences; their VKs are the
        // clearer source of truth.
        if (key.VirtualKey == Pause)
            return new KeyId(KeyCode.Pause);
        if (key.VirtualKey == PrintScreen)
            return new KeyId(KeyCode.PrintScreen);

        if (key.ScanCode != 0 && TryResolveScanCode(key.ScanCode, key.IsExtended, out var scanCode))
            return new KeyId(scanCode);

        return TryResolveKeyId(key.VirtualKey);
    }

    public static KeyId? TryResolveKeyId(ushort virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
            return new KeyId((KeyCode)((int)KeyCode.A + virtualKey - 0x41));
        if (virtualKey is >= 0x30 and <= 0x39)
            return new KeyId((KeyCode)((int)KeyCode.Digit0 + virtualKey - 0x30));
        if (virtualKey is >= F1 and <= F12)
            return new KeyId((KeyCode)((int)KeyCode.F1 + virtualKey - F1));

        var code = virtualKey switch
        {
            OemSemicolon => KeyCode.SColon,
            OemPlus => KeyCode.Colon,
            OemComma => KeyCode.Comma,
            OemPeriod => KeyCode.Dot,
            OemSlash => KeyCode.Slash,
            OemAt => KeyCode.At,
            OemMinus => KeyCode.Minus,
            OemCaret => KeyCode.Caret,
            OemYen => KeyCode.Yen,
            OemLeftBracket => KeyCode.LeftBracket,
            OemRightBracket => KeyCode.RightBracket,
            Oem102 => KeyCode.Ro,
            ZenkakuHankaku => KeyCode.ZenkakuHankaku,
            Escape => KeyCode.Escape,
            Backspace => KeyCode.Backspace,
            Tab => KeyCode.Tab,
            CapsLock => KeyCode.CapsLock,
            Enter => KeyCode.Enter,
            Space => KeyCode.Space,
            LeftShift or Shift => KeyCode.LeftShift,
            RightShift => KeyCode.RightShift,
            LeftControl or Control => KeyCode.LeftControl,
            RightControl => KeyCode.RightControl,
            LeftAlt or Alt => KeyCode.LeftAlt,
            RightAlt => KeyCode.RightAlt,
            LeftWin => KeyCode.LeftGui,
            RightWin => KeyCode.RightGui,
            Apps => KeyCode.Menu,
            PrintScreen => KeyCode.PrintScreen,
            ScrollLock => KeyCode.ScrollLock,
            Pause => KeyCode.Pause,
            Insert => KeyCode.Insert,
            Home => KeyCode.Home,
            PageUp => KeyCode.PageUp,
            Delete => KeyCode.Delete,
            End => KeyCode.End,
            PageDown => KeyCode.PageDown,
            Left => KeyCode.Left,
            Up => KeyCode.Up,
            Down => KeyCode.Down,
            Right => KeyCode.Right,
            Kana => KeyCode.KatakanaHiragana,
            Convert => KeyCode.Henkan,
            NonConvert => KeyCode.Muhenkan,
            NumLock => KeyCode.NumLock,
            NumpadSlash => KeyCode.NumpadSlash,
            NumpadAsterisk => KeyCode.NumpadAsterisk,
            NumpadMinus => KeyCode.NumpadMinus,
            Numpad7 => KeyCode.Numpad7,
            Numpad8 => KeyCode.Numpad8,
            Numpad9 => KeyCode.Numpad9,
            NumpadPlus => KeyCode.NumpadPlus,
            Numpad4 => KeyCode.Numpad4,
            Numpad5 => KeyCode.Numpad5,
            Numpad6 => KeyCode.Numpad6,
            Numpad1 => KeyCode.Numpad1,
            Numpad2 => KeyCode.Numpad2,
            Numpad3 => KeyCode.Numpad3,
            Numpad0 => KeyCode.Numpad0,
            NumpadDot => KeyCode.NumpadDot,
            NumpadComma => KeyCode.NumpadComma,
            _ => KeyCode.None
        };

        return code == KeyCode.None ? null : new KeyId(code);
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
        else if (normalized.Equals("APPSKEY", StringComparison.OrdinalIgnoreCase) || normalized.Equals("APPS", StringComparison.OrdinalIgnoreCase) || normalized.Equals("MENU", StringComparison.OrdinalIgnoreCase)) virtualKey = Apps;
        else if (normalized.Equals("SPACE", StringComparison.OrdinalIgnoreCase)) virtualKey = Space;
        else if (normalized.Equals("KANA", StringComparison.OrdinalIgnoreCase) || normalized.Equals("KATAKANAHIRAGANA", StringComparison.OrdinalIgnoreCase)) virtualKey = Kana;
        else if (normalized.Equals("HENKAN", StringComparison.OrdinalIgnoreCase) || normalized.Equals("CONVERT", StringComparison.OrdinalIgnoreCase)) virtualKey = Convert;
        else if (normalized.Equals("MUHENKAN", StringComparison.OrdinalIgnoreCase) || normalized.Equals("NONCONVERT", StringComparison.OrdinalIgnoreCase)) virtualKey = NonConvert;
        else if (normalized.Equals("ZENKAKUHANKAKU", StringComparison.OrdinalIgnoreCase) || normalized.Equals("HANKAKUZENKAKU", StringComparison.OrdinalIgnoreCase)) virtualKey = ZenkakuHankaku;
        else if (normalized.Equals("RO", StringComparison.OrdinalIgnoreCase)) virtualKey = Oem102;
        else if (normalized.Equals("YEN", StringComparison.OrdinalIgnoreCase)) virtualKey = OemYen;
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
            '^' => OemCaret,
            '\\' => Oem102,
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

    private static bool TryResolveScanCode(ushort scanCode, bool extended, out KeyCode code)
    {
        code = (scanCode, extended) switch
        {
            (0x01, false) => KeyCode.Escape,
            (0x02, false) => KeyCode.Digit1,
            (0x03, false) => KeyCode.Digit2,
            (0x04, false) => KeyCode.Digit3,
            (0x05, false) => KeyCode.Digit4,
            (0x06, false) => KeyCode.Digit5,
            (0x07, false) => KeyCode.Digit6,
            (0x08, false) => KeyCode.Digit7,
            (0x09, false) => KeyCode.Digit8,
            (0x0A, false) => KeyCode.Digit9,
            (0x0B, false) => KeyCode.Digit0,
            (0x0C, false) => KeyCode.Minus,
            (0x0D, false) => KeyCode.Caret,
            (0x0E, false) => KeyCode.Backspace,
            (0x0F, false) => KeyCode.Tab,
            (0x10, false) => KeyCode.Q,
            (0x11, false) => KeyCode.W,
            (0x12, false) => KeyCode.E,
            (0x13, false) => KeyCode.R,
            (0x14, false) => KeyCode.T,
            (0x15, false) => KeyCode.Y,
            (0x16, false) => KeyCode.U,
            (0x17, false) => KeyCode.I,
            (0x18, false) => KeyCode.O,
            (0x19, false) => KeyCode.P,
            (0x1A, false) => KeyCode.At,
            (0x1B, false) => KeyCode.LeftBracket,
            (0x1C, false) => KeyCode.Enter,
            (0x1C, true) => KeyCode.NumpadEnter,
            (0x1D, false) => KeyCode.LeftControl,
            (0x1D, true) => KeyCode.RightControl,
            (0x1E, false) => KeyCode.A,
            (0x1F, false) => KeyCode.S,
            (0x20, false) => KeyCode.D,
            (0x21, false) => KeyCode.F,
            (0x22, false) => KeyCode.G,
            (0x23, false) => KeyCode.H,
            (0x24, false) => KeyCode.J,
            (0x25, false) => KeyCode.K,
            (0x26, false) => KeyCode.L,
            (0x27, false) => KeyCode.SColon,
            (0x28, false) => KeyCode.Colon,
            (0x29, false) => KeyCode.ZenkakuHankaku,
            (0x2A, false) => KeyCode.LeftShift,
            (0x2B, false) => KeyCode.RightBracket,
            (0x2C, false) => KeyCode.Z,
            (0x2D, false) => KeyCode.X,
            (0x2E, false) => KeyCode.C,
            (0x2F, false) => KeyCode.V,
            (0x30, false) => KeyCode.B,
            (0x31, false) => KeyCode.N,
            (0x32, false) => KeyCode.M,
            (0x33, false) => KeyCode.Comma,
            (0x34, false) => KeyCode.Dot,
            (0x35, false) => KeyCode.Slash,
            (0x35, true) => KeyCode.NumpadSlash,
            (0x36, false) => KeyCode.RightShift,
            (0x37, false) => KeyCode.NumpadAsterisk,
            (0x38, false) => KeyCode.LeftAlt,
            (0x38, true) => KeyCode.RightAlt,
            (0x39, false) => KeyCode.Space,
            (0x3A, false) => KeyCode.CapsLock,
            (0x3B, false) => KeyCode.F1,
            (0x3C, false) => KeyCode.F2,
            (0x3D, false) => KeyCode.F3,
            (0x3E, false) => KeyCode.F4,
            (0x3F, false) => KeyCode.F5,
            (0x40, false) => KeyCode.F6,
            (0x41, false) => KeyCode.F7,
            (0x42, false) => KeyCode.F8,
            (0x43, false) => KeyCode.F9,
            (0x44, false) => KeyCode.F10,
            (0x45, false) => KeyCode.NumLock,
            (0x46, false) => KeyCode.ScrollLock,
            (0x47, false) => KeyCode.Numpad7,
            (0x47, true) => KeyCode.Home,
            (0x48, false) => KeyCode.Numpad8,
            (0x48, true) => KeyCode.Up,
            (0x49, false) => KeyCode.Numpad9,
            (0x49, true) => KeyCode.PageUp,
            (0x4A, false) => KeyCode.NumpadMinus,
            (0x4B, false) => KeyCode.Numpad4,
            (0x4B, true) => KeyCode.Left,
            (0x4C, false) => KeyCode.Numpad5,
            (0x4D, false) => KeyCode.Numpad6,
            (0x4D, true) => KeyCode.Right,
            (0x4E, false) => KeyCode.NumpadPlus,
            (0x4F, false) => KeyCode.Numpad1,
            (0x4F, true) => KeyCode.End,
            (0x50, false) => KeyCode.Numpad2,
            (0x50, true) => KeyCode.Down,
            (0x51, false) => KeyCode.Numpad3,
            (0x51, true) => KeyCode.PageDown,
            (0x52, false) => KeyCode.Numpad0,
            (0x52, true) => KeyCode.Insert,
            (0x53, false) => KeyCode.NumpadDot,
            (0x53, true) => KeyCode.Delete,
            (0x57, false) => KeyCode.F11,
            (0x58, false) => KeyCode.F12,
            // HID International keys used by JIS keyboards. Windows documents
            // these scan codes as Japanese-keyboard entries.
            (0x70, false) => KeyCode.KatakanaHiragana,
            (0x73, false) => KeyCode.Ro,
            (0x79, false) => KeyCode.Henkan,
            (0x7B, false) => KeyCode.Muhenkan,
            (0x7D, false) => KeyCode.Yen,
            (0x5B, true) => KeyCode.LeftGui,
            (0x5C, true) => KeyCode.RightGui,
            (0x5D, true) => KeyCode.Menu,
            _ => KeyCode.None
        };

        return code != KeyCode.None;
    }

    private static bool IsExtended(ushort virtualKey)
        => virtualKey is Left or Right or Up or Down or Home or End or PageUp or PageDown or Insert or Delete or LeftWin or RightWin or Apps or RightControl or RightAlt or NumpadSlash;
}
