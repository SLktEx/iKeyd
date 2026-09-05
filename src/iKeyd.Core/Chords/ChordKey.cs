namespace iKeyd.Core.Chords;

/// <summary>
/// Compact identities for keys that can occur on the normal iKeyd input path.
/// Values are intentionally dense so later generated lookup tables can index by
/// the numeric code directly.
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

    // Migration/import tooling may still carry arbitrary AHK identifiers. They
    // stay supported without making ordinary keyboard events string-backed.
    Custom = ushort.MaxValue
}

public readonly struct KeyId : IEquatable<KeyId>, IComparable<KeyId>
{
    private readonly string? _customValue;

    public KeyId(KeyCode code)
    {
        if (!IsCompact(code))
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

    private static bool IsCompact(KeyCode code)
        => code is >= KeyCode.A and <= KeyCode.At;

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
            "SCOLON" => KeyCode.SColon,
            "COLON" => KeyCode.Colon,
            "COMMA" => KeyCode.Comma,
            "DOT" => KeyCode.Dot,
            "SLASH" => KeyCode.Slash,
            "AT" => KeyCode.At,
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
