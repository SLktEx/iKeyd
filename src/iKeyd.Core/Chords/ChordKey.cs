namespace iKeyd.Core.Chords;

/// <summary>
/// Compact identities for keys that can occur on the normal iKeyd input path.
/// Values are intentionally dense so generated lookup tables can index by the
/// numeric code directly. Existing values are append-only to keep generated
/// profile compatibility stable.
/// </summary>
public enum KeyCode : ushort
{
    None = 0,
    A = 1,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    SColon,
    Colon,
    Comma,
    Dot,
    Slash,
    At,

    // JIS / full-size physical keyboard keys. Keep these compact as well so
    // chords on JIS-specific keys stay on the generated array-table hot path.
    Minus,
    Caret,
    Yen,
    LeftBracket,
    RightBracket,
    Ro,
    ZenkakuHankaku,
    Escape,
    Backspace,
    Tab,
    CapsLock,
    Enter,
    Space,
    LeftShift,
    RightShift,
    LeftControl,
    RightControl,
    LeftAlt,
    RightAlt,
    LeftGui,
    RightGui,
    Menu,
    PrintScreen,
    ScrollLock,
    Pause,
    Insert,
    Home,
    PageUp,
    Delete,
    End,
    PageDown,
    Left,
    Up,
    Down,
    Right,
    KatakanaHiragana,
    Henkan,
    Muhenkan,
    NumLock,
    NumpadSlash,
    NumpadAsterisk,
    NumpadMinus,
    Numpad7,
    Numpad8,
    Numpad9,
    NumpadPlus,
    Numpad4,
    Numpad5,
    Numpad6,
    Numpad1,
    Numpad2,
    Numpad3,
    NumpadEnter,
    Numpad0,
    NumpadDot,
    NumpadComma,

    // Migration/import tooling may still carry arbitrary AHK identifiers. They
    // stay supported without making ordinary keyboard events string-backed.
    Custom = ushort.MaxValue
}

public readonly struct KeyId : IEquatable<KeyId>, IComparable<KeyId>
{
    private readonly string? _customValue;

    public KeyId(KeyCode code)
    {
        if (!IsCompactCode(code))
            throw new ArgumentOutOfRangeException(nameof(code), code, "A KeyId constructed from KeyCode must use a compact key code.");

        Code = code;
        _customValue = null;
    }

    public KeyId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Key id must not be empty.", nameof(value));

        var normalized = value.Trim().ToUpperInvariant();
        if (TryParseCompactNormalized(normalized, out var code))
        {
            Code = code;
            _customValue = null;
        }
        else
        {
            Code = KeyCode.Custom;
            _customValue = normalized;
        }
    }

    public KeyCode Code { get; }
    public bool IsCompact => Code != KeyCode.Custom;

    /// <summary>
    /// Human-readable compatibility name. Compact keys return string literals;
    /// the hot input path should use <see cref="Code"/> instead.
    /// </summary>
    public string Value => Code == KeyCode.Custom
        ? _customValue ?? throw new InvalidOperationException("Custom KeyId has no value.")
        : GetCompactName(Code);

    public static bool TryParseCompact(string? value, out KeyCode code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            code = KeyCode.None;
            return false;
        }

        return TryParseCompactNormalized(value.Trim().ToUpperInvariant(), out code);
    }

    public static bool TryFromCharacter(char character, out KeyId key)
    {
        var upper = char.ToUpperInvariant(character);
        if (upper is >= 'A' and <= 'Z')
        {
            key = new KeyId((KeyCode)((int)KeyCode.A + upper - 'A'));
            return true;
        }

        if (upper is >= '0' and <= '9')
        {
            key = new KeyId((KeyCode)((int)KeyCode.Digit0 + upper - '0'));
            return true;
        }

        key = default;
        return false;
    }

    public int CompareTo(KeyId other)
    {
        if (Code != KeyCode.Custom && other.Code != KeyCode.Custom)
            return ((ushort)Code).CompareTo((ushort)other.Code);
        return string.CompareOrdinal(Value, other.Value);
    }

    public bool Equals(KeyId other)
        => Code == other.Code &&
           (Code != KeyCode.Custom || string.Equals(_customValue, other._customValue, StringComparison.Ordinal));

    public override bool Equals(object? obj) => obj is KeyId other && Equals(other);

    public override int GetHashCode()
        => Code != KeyCode.Custom
            ? (int)Code
            : HashCode.Combine((int)Code, StringComparer.Ordinal.GetHashCode(_customValue ?? string.Empty));

    public override string ToString() => Value;

    public static bool operator ==(KeyId left, KeyId right) => left.Equals(right);
    public static bool operator !=(KeyId left, KeyId right) => !left.Equals(right);

    public static implicit operator KeyId(string value) => new(value);
    public static implicit operator KeyId(KeyCode code) => new(code);

    private static bool IsCompactCode(KeyCode code)
        => code is >= KeyCode.A and <= KeyCode.NumpadComma;

    private static bool TryParseCompactNormalized(string normalized, out KeyCode code)
    {
        if (normalized.Length == 1)
        {
            var character = normalized[0];
            if (character is >= 'A' and <= 'Z')
            {
                code = (KeyCode)((int)KeyCode.A + character - 'A');
                return true;
            }

            if (character is >= '0' and <= '9')
            {
                code = (KeyCode)((int)KeyCode.Digit0 + character - '0');
                return true;
            }
        }

        if (normalized.Length is 2 or 3 && normalized[0] == 'F' &&
            int.TryParse(normalized.AsSpan(1), out var functionNumber) &&
            functionNumber is >= 1 and <= 12)
        {
            code = (KeyCode)((int)KeyCode.F1 + functionNumber - 1);
            return true;
        }

        code = normalized switch
        {
            "SCOLON" or "SEMICOLON" => KeyCode.SColon,
            "COLON" => KeyCode.Colon,
            "COMMA" => KeyCode.Comma,
            "DOT" or "PERIOD" => KeyCode.Dot,
            "SLASH" => KeyCode.Slash,
            "AT" => KeyCode.At,
            "MINUS" or "MINS" => KeyCode.Minus,
            "CARET" => KeyCode.Caret,
            "YEN" or "INT3" or "INTERNATIONAL3" => KeyCode.Yen,
            "LBRACKET" or "LEFTBRACKET" or "LBRC" => KeyCode.LeftBracket,
            "RBRACKET" or "RIGHTBRACKET" or "RBRC" => KeyCode.RightBracket,
            "RO" or "INT1" or "INTERNATIONAL1" => KeyCode.Ro,
            "ZENKAKUHANKAKU" or "HANKAKUZENKAKU" or "LANG5" or "LNG5" => KeyCode.ZenkakuHankaku,
            "ESC" or "ESCAPE" => KeyCode.Escape,
            "BS" or "BSPC" or "BACKSPACE" => KeyCode.Backspace,
            "TAB" => KeyCode.Tab,
            "CAPS" or "CAPSLOCK" => KeyCode.CapsLock,
            "ENTER" or "RETURN" => KeyCode.Enter,
            "SPACE" or "SPC" => KeyCode.Space,
            "LSHIFT" or "LSFT" => KeyCode.LeftShift,
            "RSHIFT" or "RSFT" => KeyCode.RightShift,
            "LCONTROL" or "LCTRL" or "LCTL" => KeyCode.LeftControl,
            "RCONTROL" or "RCTRL" or "RCTL" => KeyCode.RightControl,
            "LALT" => KeyCode.LeftAlt,
            "RALT" => KeyCode.RightAlt,
            "LGUI" or "LWIN" => KeyCode.LeftGui,
            "RGUI" or "RWIN" => KeyCode.RightGui,
            "MENU" or "APPS" or "APPSKEY" => KeyCode.Menu,
            "PRINTSCREEN" or "PRTSC" => KeyCode.PrintScreen,
            "SCROLLLOCK" or "SCRL" => KeyCode.ScrollLock,
            "PAUSE" => KeyCode.Pause,
            "INSERT" or "INS" => KeyCode.Insert,
            "HOME" => KeyCode.Home,
            "PAGEUP" or "PGUP" => KeyCode.PageUp,
            "DELETE" or "DEL" => KeyCode.Delete,
            "END" => KeyCode.End,
            "PAGEDOWN" or "PGDN" => KeyCode.PageDown,
            "LEFT" => KeyCode.Left,
            "UP" => KeyCode.Up,
            "DOWN" => KeyCode.Down,
            "RIGHT" => KeyCode.Right,
            "KATAKANAHIRAGANA" or "KANA" or "INT2" or "INTERNATIONAL2" => KeyCode.KatakanaHiragana,
            "HENKAN" or "CONVERT" or "INT4" or "INTERNATIONAL4" => KeyCode.Henkan,
            "MUHENKAN" or "NONCONVERT" or "INT5" or "INTERNATIONAL5" => KeyCode.Muhenkan,
            "NUMLOCK" or "NUM" => KeyCode.NumLock,
            "NUMPADSLASH" or "KP_SLASH" or "PSLS" => KeyCode.NumpadSlash,
            "NUMPADASTERISK" or "KP_ASTERISK" or "PAST" => KeyCode.NumpadAsterisk,
            "NUMPADMINUS" or "KP_MINUS" or "PMNS" => KeyCode.NumpadMinus,
            "NUMPAD7" or "KP7" or "P7" => KeyCode.Numpad7,
            "NUMPAD8" or "KP8" or "P8" => KeyCode.Numpad8,
            "NUMPAD9" or "KP9" or "P9" => KeyCode.Numpad9,
            "NUMPADPLUS" or "KP_PLUS" or "PPLS" => KeyCode.NumpadPlus,
            "NUMPAD4" or "KP4" or "P4" => KeyCode.Numpad4,
            "NUMPAD5" or "KP5" or "P5" => KeyCode.Numpad5,
            "NUMPAD6" or "KP6" or "P6" => KeyCode.Numpad6,
            "NUMPAD1" or "KP1" or "P1" => KeyCode.Numpad1,
            "NUMPAD2" or "KP2" or "P2" => KeyCode.Numpad2,
            "NUMPAD3" or "KP3" or "P3" => KeyCode.Numpad3,
            "NUMPADENTER" or "KP_ENTER" or "PENT" => KeyCode.NumpadEnter,
            "NUMPAD0" or "KP0" or "P0" => KeyCode.Numpad0,
            "NUMPADDOT" or "KP_DOT" or "PDOT" => KeyCode.NumpadDot,
            "NUMPADCOMMA" or "KP_COMMA" or "PCMM" or "INT6" or "INTERNATIONAL6" => KeyCode.NumpadComma,
            _ => KeyCode.None
        };
        return code != KeyCode.None;
    }

    private static string GetCompactName(KeyCode code)
        => code switch
        {
            KeyCode.A => "A",
            KeyCode.B => "B",
            KeyCode.C => "C",
            KeyCode.D => "D",
            KeyCode.E => "E",
            KeyCode.F => "F",
            KeyCode.G => "G",
            KeyCode.H => "H",
            KeyCode.I => "I",
            KeyCode.J => "J",
            KeyCode.K => "K",
            KeyCode.L => "L",
            KeyCode.M => "M",
            KeyCode.N => "N",
            KeyCode.O => "O",
            KeyCode.P => "P",
            KeyCode.Q => "Q",
            KeyCode.R => "R",
            KeyCode.S => "S",
            KeyCode.T => "T",
            KeyCode.U => "U",
            KeyCode.V => "V",
            KeyCode.W => "W",
            KeyCode.X => "X",
            KeyCode.Y => "Y",
            KeyCode.Z => "Z",
            KeyCode.Digit0 => "0",
            KeyCode.Digit1 => "1",
            KeyCode.Digit2 => "2",
            KeyCode.Digit3 => "3",
            KeyCode.Digit4 => "4",
            KeyCode.Digit5 => "5",
            KeyCode.Digit6 => "6",
            KeyCode.Digit7 => "7",
            KeyCode.Digit8 => "8",
            KeyCode.Digit9 => "9",
            KeyCode.F1 => "F1",
            KeyCode.F2 => "F2",
            KeyCode.F3 => "F3",
            KeyCode.F4 => "F4",
            KeyCode.F5 => "F5",
            KeyCode.F6 => "F6",
            KeyCode.F7 => "F7",
            KeyCode.F8 => "F8",
            KeyCode.F9 => "F9",
            KeyCode.F10 => "F10",
            KeyCode.F11 => "F11",
            KeyCode.F12 => "F12",
            KeyCode.SColon => "SCOLON",
            KeyCode.Colon => "COLON",
            KeyCode.Comma => "COMMA",
            KeyCode.Dot => "DOT",
            KeyCode.Slash => "SLASH",
            KeyCode.At => "AT",
            KeyCode.Minus => "MINUS",
            KeyCode.Caret => "CARET",
            KeyCode.Yen => "YEN",
            KeyCode.LeftBracket => "LBRACKET",
            KeyCode.RightBracket => "RBRACKET",
            KeyCode.Ro => "RO",
            KeyCode.ZenkakuHankaku => "ZENKAKUHANKAKU",
            KeyCode.Escape => "ESCAPE",
            KeyCode.Backspace => "BACKSPACE",
            KeyCode.Tab => "TAB",
            KeyCode.CapsLock => "CAPSLOCK",
            KeyCode.Enter => "ENTER",
            KeyCode.Space => "SPACE",
            KeyCode.LeftShift => "LSHIFT",
            KeyCode.RightShift => "RSHIFT",
            KeyCode.LeftControl => "LCONTROL",
            KeyCode.RightControl => "RCONTROL",
            KeyCode.LeftAlt => "LALT",
            KeyCode.RightAlt => "RALT",
            KeyCode.LeftGui => "LGUI",
            KeyCode.RightGui => "RGUI",
            KeyCode.Menu => "MENU",
            KeyCode.PrintScreen => "PRINTSCREEN",
            KeyCode.ScrollLock => "SCROLLLOCK",
            KeyCode.Pause => "PAUSE",
            KeyCode.Insert => "INSERT",
            KeyCode.Home => "HOME",
            KeyCode.PageUp => "PAGEUP",
            KeyCode.Delete => "DELETE",
            KeyCode.End => "END",
            KeyCode.PageDown => "PAGEDOWN",
            KeyCode.Left => "LEFT",
            KeyCode.Up => "UP",
            KeyCode.Down => "DOWN",
            KeyCode.Right => "RIGHT",
            KeyCode.KatakanaHiragana => "KATAKANAHIRAGANA",
            KeyCode.Henkan => "HENKAN",
            KeyCode.Muhenkan => "MUHENKAN",
            KeyCode.NumLock => "NUMLOCK",
            KeyCode.NumpadSlash => "NUMPADSLASH",
            KeyCode.NumpadAsterisk => "NUMPADASTERISK",
            KeyCode.NumpadMinus => "NUMPADMINUS",
            KeyCode.Numpad7 => "NUMPAD7",
            KeyCode.Numpad8 => "NUMPAD8",
            KeyCode.Numpad9 => "NUMPAD9",
            KeyCode.NumpadPlus => "NUMPADPLUS",
            KeyCode.Numpad4 => "NUMPAD4",
            KeyCode.Numpad5 => "NUMPAD5",
            KeyCode.Numpad6 => "NUMPAD6",
            KeyCode.Numpad1 => "NUMPAD1",
            KeyCode.Numpad2 => "NUMPAD2",
            KeyCode.Numpad3 => "NUMPAD3",
            KeyCode.NumpadEnter => "NUMPADENTER",
            KeyCode.Numpad0 => "NUMPAD0",
            KeyCode.NumpadDot => "NUMPADDOT",
            KeyCode.NumpadComma => "NUMPADCOMMA",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not a compact key code.")
        };
}

public readonly record struct ChordKey
{
    public ChordKey(KeyId first, KeyId second)
    {
        if (first.CompareTo(second) <= 0)
        {
            First = first;
            Second = second;
        }
        else
        {
            First = second;
            Second = first;
        }
    }

    public KeyId First { get; }
    public KeyId Second { get; }
}

public sealed record SingleMapping<TOutput>(KeyId Key, TOutput Output) where TOutput : notnull;
public sealed record ChordMapping<TOutput>(KeyId First, KeyId Second, TOutput Output) where TOutput : notnull;
