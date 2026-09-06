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
    public const ushort Kanji = 0x19;
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
    public const ushort NumpadMultiply = 0x6A;
    public const ushort NumpadAdd = 0x6B;
    public const ushort NumpadSubtract = 0x6D;
    public const ushort NumpadDecimal = 0x6E;
    public const ushort NumpadDivide = 0x6F;

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

    public const ushort VolumeMute = 0xAD;
    public const ushort VolumeDown = 0xAE;
    public const ushort VolumeUp = 0xAF;
    public const ushort MediaNext = 0xB0;
    public const ushort MediaPrevious = 0xB1;
    public const ushort MediaPlayPause = 0xB3;

    // Japanese 106/109 layout OEM keys. Keep physical-position names separate
    // from Win32's historically confusing VK_OEM_* symbolic names.
    public const ushort OemColon = 0xBA;      // VK_OEM_1: ':' / '*'
    public const ushort Oem1 = OemColon;
    public const ushort OemSemicolon = 0xBB;  // VK_OEM_PLUS: ';' / '+'
    public const ushort OemPlus = OemSemicolon;
    public const ushort OemComma = 0xBC;
    public const ushort OemMinus = 0xBD;
    public const ushort OemPeriod = 0xBE;
    public const ushort OemSlash = 0xBF;
    public const ushort OemAt = 0xC0;
    public const ushort OemLBracket = 0xDB;
    public const ushort OemYen = 0xDC;
    public const ushort OemRBracket = 0xDD;
    public const ushort OemCaret = 0xDE;
    public const ushort OemRo = 0xE2;
    public const ushort OemCopy = 0xF2;       // physical Katakana/Hiragana/Romaji key
    public const ushort OemAuto = 0xF3;       // physical Hankaku/Zenkaku/Kanji key

    private static readonly WindowsPhysicalKeyBinding[] Jis109Bindings = BuildJis109Bindings();
    private static readonly Dictionary<KeyboardKey, KeyCode> ExactPhysicalMap = BuildExactPhysicalMap();
    private static readonly Dictionary<(ushort ScanCode, bool IsExtended), KeyCode> UniqueScanMap = BuildUniqueScanMap();
    private static readonly Dictionary<ushort, KeyCode> VirtualKeyFallbackMap = BuildVirtualKeyFallbackMap();
    private static readonly Dictionary<KeyCode, KeyboardKey> OutputMap = BuildOutputMap();

    internal static IReadOnlyList<WindowsPhysicalKeyBinding> Jis109PhysicalBindings => Jis109Bindings;

    public static KeyId? TryResolveKeyId(ushort virtualKey)
        => TryResolveKeyId(Keyboard(virtualKey));

    public static KeyId? TryResolveKeyId(KeyboardKey physicalKey)
    {
        if (ExactPhysicalMap.TryGetValue(physicalKey, out var exact))
            return new KeyId(exact);

        // Low-level hooks preserve scan/extended identity. Prefer it whenever it
        // uniquely identifies a physical position, even if Windows reports an
        // unexpected VK for a Japanese OEM key.
        if (physicalKey.ScanCode != 0 &&
            UniqueScanMap.TryGetValue((physicalKey.ScanCode, physicalKey.IsExtended), out var scanned))
        {
            return new KeyId(scanned);
        }

        // Generic modifier VKs are still emitted by some synthetic/test paths.
        var generic = physicalKey.VirtualKey switch
        {
            Shift => KeyCode.LeftShift,
            Control => KeyCode.LeftCtrl,
            Alt => KeyCode.LeftAlt,
            Kana => KeyCode.Kana,
            _ => KeyCode.None
        };
        if (generic != KeyCode.None)
            return new KeyId(generic);

        if (VirtualKeyFallbackMap.TryGetValue(physicalKey.VirtualKey, out var fallback))
            return new KeyId(fallback);

        return null;
    }

    public static KeyboardKey Keyboard(ushort virtualKey)
        => new(virtualKey, 0, IsExtended(virtualKey));

    public static bool TryResolveOutputKey(KeyId keyId, out KeyboardKey key)
    {
        if (keyId.IsCompact && OutputMap.TryGetValue(keyId.Code, out key))
            return true;

        if (!keyId.IsCompact)
            return TryResolveNamedKey(keyId.Value, out key);

        key = default;
        return false;
    }

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

        // Preserve legacy AutoHotkey-style generic modifier output. New .ikeyd
        // physical names such as LCTRL/RCTRL resolve through KeyId below.
        if (normalized.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
        {
            key = Keyboard(Control);
            return true;
        }
        if (normalized.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
        {
            key = Keyboard(Shift);
            return true;
        }
        if (normalized.Equals("ALT", StringComparison.OrdinalIgnoreCase))
        {
            key = Keyboard(Alt);
            return true;
        }

        if (KeyId.TryParseCompact(normalized.ToString(), out var code) &&
            OutputMap.TryGetValue(code, out key))
        {
            return true;
        }

        key = default;
        return false;
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
            case ':': virtualKey = OemColon; break;
            case ',': virtualKey = OemComma; break;
            case '.': virtualKey = OemPeriod; break;
            case '/': virtualKey = OemSlash; break;
            case '@': virtualKey = OemAt; break;
            case '-': virtualKey = OemMinus; break;
            case '^': virtualKey = OemCaret; break;
            case '\\': virtualKey = OemYen; break;
            case '[': virtualKey = OemLBracket; break;
            case ']': virtualKey = OemRBracket; break;
            case '_': virtualKey = OemRo; break;
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

        key = new KeyboardKey(virtualKey, scanCode, IsExtended(virtualKey));
        return true;
    }

    private static WindowsPhysicalKeyBinding[] BuildJis109Bindings()
    {
        var result = new List<WindowsPhysicalKeyBinding>(109);

        Add(result, KeyCode.Escape, Escape, 0x01);
        for (var index = 0; index < 10; index++)
            Add(result, KeyCode.F1 + index, (ushort)(F1 + index), (ushort)(0x3B + index));
        Add(result, KeyCode.F11, (ushort)(F1 + 10), 0x57);
        Add(result, KeyCode.F12, F12, 0x58);
        Add(result, KeyCode.PrintScreen, PrintScreen, 0x37, true);
        Add(result, KeyCode.ScrollLock, ScrollLock, 0x46);
        Add(result, KeyCode.Pause, Pause, 0x45);

        Add(result, KeyCode.HankakuZenkaku, OemAuto, 0x29);
        var digitCodes = new[] {
            KeyCode.Digit1, KeyCode.Digit2, KeyCode.Digit3, KeyCode.Digit4, KeyCode.Digit5,
            KeyCode.Digit6, KeyCode.Digit7, KeyCode.Digit8, KeyCode.Digit9, KeyCode.Digit0
        };
        var digitVks = "1234567890";
        for (var index = 0; index < digitCodes.Length; index++)
            Add(result, digitCodes[index], digitVks[index], (ushort)(0x02 + index));
        Add(result, KeyCode.Minus, OemMinus, 0x0C);
        Add(result, KeyCode.Caret, OemCaret, 0x0D);
        Add(result, KeyCode.Yen, OemYen, 0x7D);
        Add(result, KeyCode.Backspace, Backspace, 0x0E);

        Add(result, KeyCode.Tab, Tab, 0x0F);
        AddLetterRow(result, "QWERTYUIOP", [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19]);
        Add(result, KeyCode.At, OemAt, 0x1A);
        Add(result, KeyCode.LBracket, OemLBracket, 0x1B);
        Add(result, KeyCode.Enter, Enter, 0x1C);

        Add(result, KeyCode.CapsLock, CapsLock, 0x3A);
        AddLetterRow(result, "ASDFGHJKL", [0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26]);
        Add(result, KeyCode.SColon, OemSemicolon, 0x27);
        Add(result, KeyCode.Colon, OemColon, 0x28);
        Add(result, KeyCode.RBracket, OemRBracket, 0x2B);

        Add(result, KeyCode.LeftShift, LeftShift, 0x2A);
        AddLetterRow(result, "ZXCVBNM", [0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32]);
        Add(result, KeyCode.Comma, OemComma, 0x33);
        Add(result, KeyCode.Dot, OemPeriod, 0x34);
        Add(result, KeyCode.Slash, OemSlash, 0x35);
        Add(result, KeyCode.Ro, OemRo, 0x73);
        Add(result, KeyCode.RightShift, RightShift, 0x36);

        Add(result, KeyCode.LeftCtrl, LeftControl, 0x1D);
        Add(result, KeyCode.LeftWin, LeftWin, 0x5B, true);
        Add(result, KeyCode.LeftAlt, LeftAlt, 0x38);
        Add(result, KeyCode.NonConvert, NonConvert, 0x7B);
        Add(result, KeyCode.Space, Space, 0x39);
        Add(result, KeyCode.Convert, Convert, 0x79);
        Add(result, KeyCode.Kana, OemCopy, 0x70);
        Add(result, KeyCode.RightAlt, RightAlt, 0x38, true);
        Add(result, KeyCode.RightWin, RightWin, 0x5C, true);
        Add(result, KeyCode.Apps, Apps, 0x5D, true);
        Add(result, KeyCode.RightCtrl, RightControl, 0x1D, true);

        Add(result, KeyCode.Insert, Insert, 0x52, true);
        Add(result, KeyCode.Home, Home, 0x47, true);
        Add(result, KeyCode.PageUp, PageUp, 0x49, true);
        Add(result, KeyCode.Delete, Delete, 0x53, true);
        Add(result, KeyCode.End, End, 0x4F, true);
        Add(result, KeyCode.PageDown, PageDown, 0x51, true);
        Add(result, KeyCode.Up, Up, 0x48, true);
        Add(result, KeyCode.Left, Left, 0x4B, true);
        Add(result, KeyCode.Down, Down, 0x50, true);
        Add(result, KeyCode.Right, Right, 0x4D, true);

        Add(result, KeyCode.NumLock, NumLock, 0x45);
        Add(result, KeyCode.NumpadDivide, NumpadDivide, 0x35, true);
        Add(result, KeyCode.NumpadMultiply, NumpadMultiply, 0x37);
        Add(result, KeyCode.NumpadSubtract, NumpadSubtract, 0x4A);
        Add(result, KeyCode.Numpad7, Numpad7, 0x47);
        Add(result, KeyCode.Numpad8, Numpad8, 0x48);
        Add(result, KeyCode.Numpad9, Numpad9, 0x49);
        Add(result, KeyCode.NumpadAdd, NumpadAdd, 0x4E);
        Add(result, KeyCode.Numpad4, Numpad4, 0x4B);
        Add(result, KeyCode.Numpad5, Numpad5, 0x4C);
        Add(result, KeyCode.Numpad6, Numpad6, 0x4D);
        Add(result, KeyCode.Numpad1, Numpad1, 0x4F);
        Add(result, KeyCode.Numpad2, Numpad2, 0x50);
        Add(result, KeyCode.Numpad3, Numpad3, 0x51);
        Add(result, KeyCode.NumpadEnter, Enter, 0x1C, true);
        Add(result, KeyCode.Numpad0, Numpad0, 0x52);
        Add(result, KeyCode.NumpadDecimal, NumpadDecimal, 0x53);

        var expected = Jis109PhysicalKeyRegistry.Keys.Select(item => item.Code).ToHashSet();
        var actual = result.Select(item => item.Code).ToHashSet();
        if (result.Count != 109 || !expected.SetEquals(actual))
        {
            var missing = string.Join(", ", expected.Except(actual));
            var extra = string.Join(", ", actual.Except(expected));
            throw new InvalidOperationException(
                $"Windows JIS109 mapping is asymmetric (count={result.Count}, missing=[{missing}], extra=[{extra}]).");
        }

        return result.ToArray();
    }

    private static void AddLetterRow(List<WindowsPhysicalKeyBinding> result, string letters, ushort[] scanCodes)
    {
        if (letters.Length != scanCodes.Length)
            throw new ArgumentException("Letter row and scan-code table lengths must match.");

        for (var index = 0; index < letters.Length; index++)
        {
            if (!KeyId.TryFromCharacter(letters[index], out var key))
                throw new InvalidOperationException($"Unable to create physical identity for '{letters[index]}'.");
            Add(result, key.Code, letters[index], scanCodes[index]);
        }
    }

    private static void Add(
        List<WindowsPhysicalKeyBinding> result,
        KeyCode code,
        int virtualKey,
        int scanCode,
        bool isExtended = false)
        => result.Add(new WindowsPhysicalKeyBinding(
            code,
            new KeyboardKey(checked((ushort)virtualKey), checked((ushort)scanCode), isExtended)));

    private static Dictionary<KeyboardKey, KeyCode> BuildExactPhysicalMap()
    {
        var result = new Dictionary<KeyboardKey, KeyCode>();
        foreach (var binding in Jis109Bindings)
        {
            if (!result.TryAdd(binding.WindowsKey, binding.Code))
                throw new InvalidOperationException($"Duplicate Windows physical key signature for {binding.Code}.");
        }
        return result;
    }

    private static Dictionary<(ushort ScanCode, bool IsExtended), KeyCode> BuildUniqueScanMap()
    {
        var result = new Dictionary<(ushort, bool), KeyCode>();
        var ambiguous = new HashSet<(ushort, bool)>();
        foreach (var binding in Jis109Bindings)
        {
            if (binding.WindowsKey.ScanCode == 0)
                continue;
            var signature = (binding.WindowsKey.ScanCode, binding.WindowsKey.IsExtended);
            if (!result.TryAdd(signature, binding.Code) && result[signature] != binding.Code)
                ambiguous.Add(signature);
        }
        foreach (var signature in ambiguous)
            result.Remove(signature);
        return result;
    }

    private static Dictionary<ushort, KeyCode> BuildVirtualKeyFallbackMap()
    {
        var result = new Dictionary<ushort, KeyCode>();
        foreach (var binding in Jis109Bindings)
            result.TryAdd(binding.WindowsKey.VirtualKey, binding.Code);

        // Multimedia keys are outside the JIS109 physical count but supported by
        // the same semantic input/output path.
        result[VolumeUp] = KeyCode.VolumeUp;
        result[VolumeDown] = KeyCode.VolumeDown;
        result[VolumeMute] = KeyCode.VolumeMute;
        result[MediaPlayPause] = KeyCode.MediaPlayPause;
        result[MediaNext] = KeyCode.MediaNext;
        result[MediaPrevious] = KeyCode.MediaPrevious;
        return result;
    }

    private static Dictionary<KeyCode, KeyboardKey> BuildOutputMap()
    {
        var result = Jis109Bindings.ToDictionary(item => item.Code, item => item.WindowsKey);
        result[KeyCode.VolumeUp] = Keyboard(VolumeUp);
        result[KeyCode.VolumeDown] = Keyboard(VolumeDown);
        result[KeyCode.VolumeMute] = Keyboard(VolumeMute);
        result[KeyCode.MediaPlayPause] = Keyboard(MediaPlayPause);
        result[KeyCode.MediaNext] = Keyboard(MediaNext);
        result[KeyCode.MediaPrevious] = Keyboard(MediaPrevious);
        return result;
    }

    private static bool IsExtended(ushort virtualKey)
        => virtualKey is PageUp or PageDown or End or Home or Left or Up or Right or Down or
            Insert or Delete or PrintScreen or LeftWin or RightWin or Apps or RightControl or RightAlt or NumpadDivide;
}

internal readonly record struct WindowsPhysicalKeyBinding(KeyCode Code, KeyboardKey WindowsKey);
