namespace iKeyd.Core.Chords;

/// <summary>
/// Dense, target-neutral identities for physical keys that can occur on the
/// normal iKeyd input path. Values stay contiguous so generated keymap tables
/// can index by the numeric code directly.
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

    // JIS punctuation positions. These names identify physical positions, not
    // whichever character the active keyboard layout happens to produce.
    SColon,
    Colon,
    Comma,
    Dot,
    Slash,
    At,
    Minus,
    Caret,
    Yen,
    LBracket,
    RBracket,
    Ro,

    // Ordinary/JIS control surface.
    Space,
    Tab,
    Enter,
    Backspace,
    Escape,
    CapsLock,
    Kana,
    Convert,
    NonConvert,
    HankakuZenkaku,

    // Navigation/editing.
    Insert,
    Delete,
    Home,
    End,
    PageUp,
    PageDown,
    Left,
    Up,
    Right,
    Down,

    // Sided modifiers/system keys.
    LeftShift,
    RightShift,
    LeftCtrl,
    RightCtrl,
    LeftAlt,
    RightAlt,
    LeftWin,
    RightWin,
    Apps,
    PrintScreen,
    ScrollLock,
    Pause,

    // Numpad. Main number-row identities intentionally remain Digit0..Digit9.
    NumLock,
    Numpad0,
    Numpad1,
    Numpad2,
    Numpad3,
    Numpad4,
    Numpad5,
    Numpad6,
    Numpad7,
    Numpad8,
    Numpad9,
    NumpadDecimal,
    NumpadDivide,
    NumpadMultiply,
    NumpadSubtract,
    NumpadAdd,
    NumpadEnter,

    // Keyboard multimedia keys supported by the Windows backend. They are not
    // part of the 109-key physical geometry but share the same semantic key id.
    VolumeUp,
    VolumeDown,
    VolumeMute,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,

    // Migration/import tooling may still carry arbitrary AHK identifiers. They
    // stay supported without making ordinary keyboard events string-backed.
    Custom = ushort.MaxValue
}

public readonly struct KeyId : IEquatable<KeyId>, IComparable<KeyId>
{
    public const KeyCode LastCompactCode = KeyCode.MediaPrevious;

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
    public bool IsCompact => IsCompactCode(Code);

    /// <summary>
    /// Human-readable canonical authoring name. Compact keys return stable names;
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
        => code is >= KeyCode.A and <= LastCompactCode;

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
            "MINUS" => KeyCode.Minus,
            "CARET" or "HAT" => KeyCode.Caret,
            "YEN" or "BACKSLASH" => KeyCode.Yen,
            "LBRACKET" or "LEFTBRACKET" => KeyCode.LBracket,
            "RBRACKET" or "RIGHTBRACKET" => KeyCode.RBracket,
            "RO" or "OEM102" => KeyCode.Ro,

            "SPACE" => KeyCode.Space,
            "TAB" => KeyCode.Tab,
            "ENTER" or "RETURN" => KeyCode.Enter,
            "BS" or "BACKSPACE" => KeyCode.Backspace,
            "ESC" or "ESCAPE" => KeyCode.Escape,
            "CAPS" or "CAPSLOCK" => KeyCode.CapsLock,
            "KANA" or "HIRAGANA" or "KATAKANAHIRAGANA" or "KATAKANAHIRAGANAROMAJI" => KeyCode.Kana,
            "CONVERT" or "HENKAN" => KeyCode.Convert,
            "NONCONVERT" or "MUHENKAN" => KeyCode.NonConvert,
            "HANKAKUZENKAKU" or "ZENKAKUHANKAKU" => KeyCode.HankakuZenkaku,

            "INS" or "INSERT" => KeyCode.Insert,
            "DEL" or "DELETE" => KeyCode.Delete,
            "HOME" => KeyCode.Home,
            "END" => KeyCode.End,
            "PGUP" or "PAGEUP" => KeyCode.PageUp,
            "PGDN" or "PAGEDOWN" => KeyCode.PageDown,
            "LEFT" or "ARROWLEFT" => KeyCode.Left,
            "UP" or "ARROWUP" => KeyCode.Up,
            "RIGHT" or "ARROWRIGHT" => KeyCode.Right,
            "DOWN" or "ARROWDOWN" => KeyCode.Down,

            "SHIFT" or "LSHIFT" or "LEFTSHIFT" => KeyCode.LeftShift,
            "RSHIFT" or "RIGHTSHIFT" => KeyCode.RightShift,
            "CTRL" or "CONTROL" or "LCTRL" or "LCONTROL" or "LEFTCTRL" or "LEFTCONTROL" => KeyCode.LeftCtrl,
            "RCTRL" or "RCONTROL" or "RIGHTCTRL" or "RIGHTCONTROL" => KeyCode.RightCtrl,
            "ALT" or "LALT" or "LEFTALT" => KeyCode.LeftAlt,
            "RALT" or "RIGHTALT" or "ALTGR" => KeyCode.RightAlt,
            "WIN" or "GUI" or "LWIN" or "LEFTWIN" => KeyCode.LeftWin,
            "RWIN" or "RIGHTWIN" => KeyCode.RightWin,
            "APPS" or "APPSKEY" or "MENU" => KeyCode.Apps,
            "PRINTSCREEN" or "PRTSC" or "PRTSCN" => KeyCode.PrintScreen,
            "SCROLLLOCK" or "SCROLL" => KeyCode.ScrollLock,
            "PAUSE" or "BREAK" => KeyCode.Pause,

            "NUMLOCK" => KeyCode.NumLock,
            "NUMPAD0" => KeyCode.Numpad0,
            "NUMPAD1" => KeyCode.Numpad1,
            "NUMPAD2" => KeyCode.Numpad2,
            "NUMPAD3" => KeyCode.Numpad3,
            "NUMPAD4" => KeyCode.Numpad4,
            "NUMPAD5" => KeyCode.Numpad5,
            "NUMPAD6" => KeyCode.Numpad6,
            "NUMPAD7" => KeyCode.Numpad7,
            "NUMPAD8" => KeyCode.Numpad8,
            "NUMPAD9" => KeyCode.Numpad9,
            "NUMPADDECIMAL" or "NUMPADDOT" => KeyCode.NumpadDecimal,
            "NUMPADDIVIDE" or "NUMPADSLASH" => KeyCode.NumpadDivide,
            "NUMPADMULTIPLY" or "NUMPADASTERISK" => KeyCode.NumpadMultiply,
            "NUMPADSUBTRACT" or "NUMPADMINUS" => KeyCode.NumpadSubtract,
            "NUMPADADD" or "NUMPADPLUS" => KeyCode.NumpadAdd,
            "NUMPADENTER" => KeyCode.NumpadEnter,

            "VOLUMEUP" or "VOLUME_UP" => KeyCode.VolumeUp,
            "VOLUMEDOWN" or "VOLUME_DOWN" => KeyCode.VolumeDown,
            "VOLUMEMUTE" or "VOLUME_MUTE" => KeyCode.VolumeMute,
            "MEDIAPLAYPAUSE" or "MEDIA_PLAY_PAUSE" => KeyCode.MediaPlayPause,
            "MEDIANEXT" or "MEDIA_NEXT" => KeyCode.MediaNext,
            "MEDIAPREV" or "MEDIAPREVIOUS" or "MEDIA_PREV" or "MEDIA_PREVIOUS" => KeyCode.MediaPrevious,
            _ => KeyCode.None
        };
        return code != KeyCode.None;
    }

    private static string GetCompactName(KeyCode code)
    {
        if (code is >= KeyCode.A and <= KeyCode.Z)
            return ((char)('A' + (int)code - (int)KeyCode.A)).ToString();
        if (code is >= KeyCode.Digit0 and <= KeyCode.Digit9)
            return ((char)('0' + (int)code - (int)KeyCode.Digit0)).ToString();
        if (code is >= KeyCode.F1 and <= KeyCode.F12)
            return $"F{1 + (int)code - (int)KeyCode.F1}";

        return code switch
        {
            KeyCode.SColon => "SCOLON",
            KeyCode.Colon => "COLON",
            KeyCode.Comma => "COMMA",
            KeyCode.Dot => "DOT",
            KeyCode.Slash => "SLASH",
            KeyCode.At => "AT",
            KeyCode.Minus => "MINUS",
            KeyCode.Caret => "CARET",
            KeyCode.Yen => "YEN",
            KeyCode.LBracket => "LBRACKET",
            KeyCode.RBracket => "RBRACKET",
            KeyCode.Ro => "RO",
            KeyCode.Space => "SPACE",
            KeyCode.Tab => "TAB",
            KeyCode.Enter => "ENTER",
            KeyCode.Backspace => "BACKSPACE",
            KeyCode.Escape => "ESCAPE",
            KeyCode.CapsLock => "CAPSLOCK",
            KeyCode.Kana => "KANA",
            KeyCode.Convert => "CONVERT",
            KeyCode.NonConvert => "NONCONVERT",
            KeyCode.HankakuZenkaku => "HANKAKUZENKAKU",
            KeyCode.Insert => "INSERT",
            KeyCode.Delete => "DELETE",
            KeyCode.Home => "HOME",
            KeyCode.End => "END",
            KeyCode.PageUp => "PAGEUP",
            KeyCode.PageDown => "PAGEDOWN",
            KeyCode.Left => "LEFT",
            KeyCode.Up => "UP",
            KeyCode.Right => "RIGHT",
            KeyCode.Down => "DOWN",
            KeyCode.LeftShift => "LSHIFT",
            KeyCode.RightShift => "RSHIFT",
            KeyCode.LeftCtrl => "LCTRL",
            KeyCode.RightCtrl => "RCTRL",
            KeyCode.LeftAlt => "LALT",
            KeyCode.RightAlt => "RALT",
            KeyCode.LeftWin => "LWIN",
            KeyCode.RightWin => "RWIN",
            KeyCode.Apps => "APPS",
            KeyCode.PrintScreen => "PRINTSCREEN",
            KeyCode.ScrollLock => "SCROLLLOCK",
            KeyCode.Pause => "PAUSE",
            KeyCode.NumLock => "NUMLOCK",
            KeyCode.Numpad0 => "NUMPAD0",
            KeyCode.Numpad1 => "NUMPAD1",
            KeyCode.Numpad2 => "NUMPAD2",
            KeyCode.Numpad3 => "NUMPAD3",
            KeyCode.Numpad4 => "NUMPAD4",
            KeyCode.Numpad5 => "NUMPAD5",
            KeyCode.Numpad6 => "NUMPAD6",
            KeyCode.Numpad7 => "NUMPAD7",
            KeyCode.Numpad8 => "NUMPAD8",
            KeyCode.Numpad9 => "NUMPAD9",
            KeyCode.NumpadDecimal => "NUMPADDECIMAL",
            KeyCode.NumpadDivide => "NUMPADDIVIDE",
            KeyCode.NumpadMultiply => "NUMPADMULTIPLY",
            KeyCode.NumpadSubtract => "NUMPADSUBTRACT",
            KeyCode.NumpadAdd => "NUMPADADD",
            KeyCode.NumpadEnter => "NUMPADENTER",
            KeyCode.VolumeUp => "VOLUME_UP",
            KeyCode.VolumeDown => "VOLUME_DOWN",
            KeyCode.VolumeMute => "VOLUME_MUTE",
            KeyCode.MediaPlayPause => "MEDIA_PLAY_PAUSE",
            KeyCode.MediaNext => "MEDIA_NEXT",
            KeyCode.MediaPrevious => "MEDIA_PREV",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not a compact key code.")
        };
    }
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
