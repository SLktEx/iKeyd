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

        var trimmed = value.AsSpan().Trim();
        if (TryParseCompact(trimmed, out var code))
        {
            Code = code;
            _customValue = null;
        }
        else
        {
            Code = KeyCode.Custom;
            _customValue = trimmed.ToString().ToUpperInvariant();
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

        return TryParseCompact(value.AsSpan(), out code);
    }

    /// <summary>
    /// Allocation-free parser for the canonical key universe. This overload is
    /// used by input/output hot paths that already operate on spans.
    /// </summary>
    public static bool TryParseCompact(ReadOnlySpan<char> value, out KeyCode code)
    {
        var normalized = value.Trim();
        if (normalized.IsEmpty)
        {
            code = KeyCode.None;
            return false;
        }

        if (normalized.Length == 1)
        {
            var character = char.ToUpperInvariant(normalized[0]);
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

        // Enum.TryParse(ReadOnlySpan<char>) is allocation-free and covers every
        // canonical enum spelling (F1, NumpadEnter, RightCtrl, ...). Reject
        // numeric enum syntax so strings such as "42" cannot accidentally become
        // physical key identities.
        if (!char.IsDigit(normalized[0]) && normalized[0] is not ('+' or '-') &&
            Enum.TryParse<KeyCode>(normalized, ignoreCase: true, out var parsed) &&
            IsCompactCode(parsed))
        {
            code = parsed;
            return true;
        }

        if (Equals(normalized, "SEMICOLON")) { code = KeyCode.SColon; return true; }
        if (Equals(normalized, "PERIOD")) { code = KeyCode.Dot; return true; }
        if (Equals(normalized, "HAT")) { code = KeyCode.Caret; return true; }
        if (Equals(normalized, "BACKSLASH")) { code = KeyCode.Yen; return true; }
        if (Equals(normalized, "LEFTBRACKET")) { code = KeyCode.LBracket; return true; }
        if (Equals(normalized, "RIGHTBRACKET")) { code = KeyCode.RBracket; return true; }
        if (Equals(normalized, "OEM102")) { code = KeyCode.Ro; return true; }

        if (Equals(normalized, "RETURN")) { code = KeyCode.Enter; return true; }
        if (Equals(normalized, "BS")) { code = KeyCode.Backspace; return true; }
        if (Equals(normalized, "ESC")) { code = KeyCode.Escape; return true; }
        if (Equals(normalized, "CAPS")) { code = KeyCode.CapsLock; return true; }
        if (Equals(normalized, "HIRAGANA") || Equals(normalized, "KATAKANAHIRAGANA") || Equals(normalized, "KATAKANAHIRAGANAROMAJI")) { code = KeyCode.Kana; return true; }
        if (Equals(normalized, "HENKAN")) { code = KeyCode.Convert; return true; }
        if (Equals(normalized, "MUHENKAN")) { code = KeyCode.NonConvert; return true; }
        if (Equals(normalized, "ZENKAKUHANKAKU")) { code = KeyCode.HankakuZenkaku; return true; }

        if (Equals(normalized, "INS")) { code = KeyCode.Insert; return true; }
        if (Equals(normalized, "DEL")) { code = KeyCode.Delete; return true; }
        if (Equals(normalized, "PGUP")) { code = KeyCode.PageUp; return true; }
        if (Equals(normalized, "PGDN")) { code = KeyCode.PageDown; return true; }
        if (Equals(normalized, "ARROWLEFT")) { code = KeyCode.Left; return true; }
        if (Equals(normalized, "ARROWUP")) { code = KeyCode.Up; return true; }
        if (Equals(normalized, "ARROWRIGHT")) { code = KeyCode.Right; return true; }
        if (Equals(normalized, "ARROWDOWN")) { code = KeyCode.Down; return true; }

        if (Equals(normalized, "SHIFT") || Equals(normalized, "LSHIFT")) { code = KeyCode.LeftShift; return true; }
        if (Equals(normalized, "RSHIFT")) { code = KeyCode.RightShift; return true; }
        if (Equals(normalized, "CTRL") || Equals(normalized, "CONTROL") || Equals(normalized, "LCTRL") || Equals(normalized, "LCONTROL") || Equals(normalized, "LEFTCONTROL")) { code = KeyCode.LeftCtrl; return true; }
        if (Equals(normalized, "RCTRL") || Equals(normalized, "RCONTROL") || Equals(normalized, "RIGHTCONTROL")) { code = KeyCode.RightCtrl; return true; }
        if (Equals(normalized, "ALT") || Equals(normalized, "LALT")) { code = KeyCode.LeftAlt; return true; }
        if (Equals(normalized, "RALT") || Equals(normalized, "ALTGR")) { code = KeyCode.RightAlt; return true; }
        if (Equals(normalized, "WIN") || Equals(normalized, "GUI") || Equals(normalized, "LWIN")) { code = KeyCode.LeftWin; return true; }
        if (Equals(normalized, "RWIN")) { code = KeyCode.RightWin; return true; }
        if (Equals(normalized, "APPSKEY") || Equals(normalized, "MENU")) { code = KeyCode.Apps; return true; }
        if (Equals(normalized, "PRTSC") || Equals(normalized, "PRTSCN")) { code = KeyCode.PrintScreen; return true; }
        if (Equals(normalized, "SCROLL")) { code = KeyCode.ScrollLock; return true; }
        if (Equals(normalized, "BREAK")) { code = KeyCode.Pause; return true; }

        if (Equals(normalized, "NUMPADDOT")) { code = KeyCode.NumpadDecimal; return true; }
        if (Equals(normalized, "NUMPADSLASH")) { code = KeyCode.NumpadDivide; return true; }
        if (Equals(normalized, "NUMPADASTERISK")) { code = KeyCode.NumpadMultiply; return true; }
        if (Equals(normalized, "NUMPADMINUS")) { code = KeyCode.NumpadSubtract; return true; }
        if (Equals(normalized, "NUMPADPLUS")) { code = KeyCode.NumpadAdd; return true; }

        if (Equals(normalized, "VOLUME_UP")) { code = KeyCode.VolumeUp; return true; }
        if (Equals(normalized, "VOLUME_DOWN")) { code = KeyCode.VolumeDown; return true; }
        if (Equals(normalized, "VOLUME_MUTE")) { code = KeyCode.VolumeMute; return true; }
        if (Equals(normalized, "MEDIA_PLAY_PAUSE")) { code = KeyCode.MediaPlayPause; return true; }
        if (Equals(normalized, "MEDIA_NEXT")) { code = KeyCode.MediaNext; return true; }
        if (Equals(normalized, "MEDIAPREV") || Equals(normalized, "MEDIA_PREV") || Equals(normalized, "MEDIA_PREVIOUS")) { code = KeyCode.MediaPrevious; return true; }

        code = KeyCode.None;
        return false;
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

    private static bool Equals(ReadOnlySpan<char> value, string expected)
        => value.Equals(expected, StringComparison.OrdinalIgnoreCase);

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
