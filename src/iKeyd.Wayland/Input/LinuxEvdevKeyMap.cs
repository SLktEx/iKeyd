using iKeyd.Core.Input;

namespace iKeyd.Wayland.Input;

public sealed class LinuxEvdevKeyMap
{
    private readonly Dictionary<ushort, ushort> _evdevToVirtual;
    private readonly Dictionary<ushort, ushort> _virtualToEvdev;

    public LinuxEvdevKeyMap(IReadOnlyDictionary<ushort, ushort>? overrides = null)
    {
        _evdevToVirtual = BuildDefaultMap();
        if (overrides is not null)
        {
            foreach (var pair in overrides)
                _evdevToVirtual[pair.Key] = pair.Value;
        }

        _virtualToEvdev = [];
        foreach (var pair in _evdevToVirtual)
            _virtualToEvdev.TryAdd(pair.Value, pair.Key);
    }

    public bool TryFromEvdev(ushort evdevCode, out KeyboardKey key)
    {
        if (_evdevToVirtual.TryGetValue(evdevCode, out var virtualKey))
        {
            key = new KeyboardKey(virtualKey, evdevCode, IsExtended(virtualKey));
            return true;
        }

        key = default;
        return false;
    }

    public bool TryToEvdev(KeyboardKey key, out ushort evdevCode)
    {
        if (key.ScanCode != 0 && _evdevToVirtual.ContainsKey(key.ScanCode))
        {
            evdevCode = key.ScanCode;
            return true;
        }

        return _virtualToEvdev.TryGetValue(key.VirtualKey, out evdevCode);
    }

    public bool TryGetAsciiStroke(char character, out ushort evdevCode, out bool shift)
    {
        shift = false;
        var lower = char.ToLowerInvariant(character);
        if (lower is >= 'a' and <= 'z')
        {
            var virtualKey = (ushort)char.ToUpperInvariant(lower);
            shift = char.IsUpper(character);
            return _virtualToEvdev.TryGetValue(virtualKey, out evdevCode);
        }

        if (character is >= '0' and <= '9')
        {
            var virtualKey = (ushort)character;
            return _virtualToEvdev.TryGetValue(virtualKey, out evdevCode);
        }

        (ushort Code, bool Shift) stroke = character switch
        {
            ' ' => (LinuxInputCodes.KeySpace, false),
            '\n' or '\r' => (LinuxInputCodes.KeyEnter, false),
            '-' => (LinuxInputCodes.KeyMinus, false),
            '_' => (LinuxInputCodes.KeyMinus, true),
            '=' => (LinuxInputCodes.KeyEqual, false),
            '+' => (LinuxInputCodes.KeyEqual, true),
            ',' => (LinuxInputCodes.KeyComma, false),
            '<' => (LinuxInputCodes.KeyComma, true),
            '.' => (LinuxInputCodes.KeyDot, false),
            '>' => (LinuxInputCodes.KeyDot, true),
            '/' => (LinuxInputCodes.KeySlash, false),
            '?' => (LinuxInputCodes.KeySlash, true),
            ';' => (LinuxInputCodes.KeySemicolon, false),
            ':' => (LinuxInputCodes.KeySemicolon, true),
            '\'' => (LinuxInputCodes.KeyApostrophe, false),
            '"' => (LinuxInputCodes.KeyApostrophe, true),
            '[' => (LinuxInputCodes.KeyLeftBrace, false),
            '{' => (LinuxInputCodes.KeyLeftBrace, true),
            ']' => (LinuxInputCodes.KeyRightBrace, false),
            '}' => (LinuxInputCodes.KeyRightBrace, true),
            '\\' => (LinuxInputCodes.KeyBackslash, false),
            '|' => (LinuxInputCodes.KeyBackslash, true),
            '`' => (LinuxInputCodes.KeyGrave, false),
            '~' => (LinuxInputCodes.KeyGrave, true),
            '!' => (LinuxInputCodes.Key1, true),
            '@' => (3, true),
            '#' => (4, true),
            '$' => (5, true),
            '%' => (6, true),
            '^' => (7, true),
            '&' => (8, true),
            '*' => (9, true),
            '(' => (10, true),
            ')' => (11, true),
            _ => default
        };

        if (stroke.Code == 0)
        {
            evdevCode = 0;
            shift = false;
            return false;
        }

        evdevCode = stroke.Code;
        shift = stroke.Shift;
        return true;
    }

    private static Dictionary<ushort, ushort> BuildDefaultMap()
    {
        var map = new Dictionary<ushort, ushort>
        {
            [LinuxInputCodes.KeyEsc] = 0x1B,
            [LinuxInputCodes.KeyMinus] = 0xBD,
            [LinuxInputCodes.KeyEqual] = 0xBB,
            [LinuxInputCodes.KeyBackspace] = 0x08,
            [LinuxInputCodes.KeyTab] = 0x09,
            [LinuxInputCodes.KeyLeftBrace] = 0xC0,
            [LinuxInputCodes.KeyRightBrace] = 0xDB,
            [LinuxInputCodes.KeyEnter] = 0x0D,
            [LinuxInputCodes.KeyLeftCtrl] = 0x11,
            [LinuxInputCodes.KeySemicolon] = 0xBA,
            [LinuxInputCodes.KeyApostrophe] = 0xBB,
            [LinuxInputCodes.KeyGrave] = 0xC0,
            [LinuxInputCodes.KeyLeftShift] = 0x10,
            [LinuxInputCodes.KeyBackslash] = 0xDC,
            [LinuxInputCodes.KeyComma] = 0xBC,
            [LinuxInputCodes.KeyDot] = 0xBE,
            [LinuxInputCodes.KeySlash] = 0xBF,
            [LinuxInputCodes.KeyRightShift] = 0x10,
            [LinuxInputCodes.KeyLeftAlt] = 0x12,
            [LinuxInputCodes.KeySpace] = 0x20,
            [LinuxInputCodes.KeyCapsLock] = 0x14,
            [LinuxInputCodes.KeyHenkan] = 0x1C,
            [LinuxInputCodes.KeyKatakanaHiragana] = 0x15,
            [LinuxInputCodes.KeyMuhenkan] = 0x1D,
            [LinuxInputCodes.KeyRightCtrl] = 0x11,
            [LinuxInputCodes.KeyRightAlt] = 0x12,
            [LinuxInputCodes.KeyHome] = 0x24,
            [LinuxInputCodes.KeyUp] = 0x26,
            [LinuxInputCodes.KeyPageUp] = 0x21,
            [LinuxInputCodes.KeyLeft] = 0x25,
            [LinuxInputCodes.KeyRight] = 0x27,
            [LinuxInputCodes.KeyEnd] = 0x23,
            [LinuxInputCodes.KeyDown] = 0x28,
            [LinuxInputCodes.KeyPageDown] = 0x22,
            [LinuxInputCodes.KeyInsert] = 0x2D,
            [LinuxInputCodes.KeyDelete] = 0x2E,
            [LinuxInputCodes.KeyLeftMeta] = 0x5B,
            [LinuxInputCodes.KeyRightMeta] = 0x5C,
            [LinuxInputCodes.KeyMenu] = 0x5D
        };

        for (var digit = 1; digit <= 9; digit++)
            map[(ushort)(LinuxInputCodes.Key1 + digit - 1)] = (ushort)('0' + digit);
        map[LinuxInputCodes.Key0] = (ushort)'0';

        var qwerty = "QWERTYUIOP";
        for (var index = 0; index < qwerty.Length; index++)
            map[(ushort)(LinuxInputCodes.KeyQ + index)] = qwerty[index];
        var home = "ASDFGHJKL";
        for (var index = 0; index < home.Length; index++)
            map[(ushort)(LinuxInputCodes.KeyA + index)] = home[index];
        var bottom = "ZXCVBNM";
        for (var index = 0; index < bottom.Length; index++)
            map[(ushort)(LinuxInputCodes.KeyZ + index)] = bottom[index];

        for (var index = 0; index < 10; index++)
            map[(ushort)(LinuxInputCodes.KeyF1 + index)] = (ushort)(0x70 + index);
        map[LinuxInputCodes.KeyF11] = 0x7A;
        map[LinuxInputCodes.KeyF12] = 0x7B;

        return map;
    }

    private static bool IsExtended(ushort virtualKey)
        => virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D;
}
