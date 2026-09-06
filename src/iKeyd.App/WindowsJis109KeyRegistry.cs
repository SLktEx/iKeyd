using iKeyd.Core.Chords;
using iKeyd.Core.Input;

namespace iKeyd.App;

internal static class WindowsJis109KeyRegistry
{
    private static readonly WindowsJis109Key[] Registry =
    [
        new(KeyCode.Escape, 0x1B, 0x01),
        new(KeyCode.F1, 0x70, 0x3B),
        new(KeyCode.F2, 0x71, 0x3C),
        new(KeyCode.F3, 0x72, 0x3D),
        new(KeyCode.F4, 0x73, 0x3E),
        new(KeyCode.F5, 0x74, 0x3F),
        new(KeyCode.F6, 0x75, 0x40),
        new(KeyCode.F7, 0x76, 0x41),
        new(KeyCode.F8, 0x77, 0x42),
        new(KeyCode.F9, 0x78, 0x43),
        new(KeyCode.F10, 0x79, 0x44),
        new(KeyCode.F11, 0x7A, 0x57),
        new(KeyCode.F12, 0x7B, 0x58),
        new(KeyCode.PrintScreen, 0x2C, 0x37, extended: true),
        new(KeyCode.ScrollLock, 0x91, 0x46),
        new(KeyCode.Pause, 0x13, 0x45),

        new(KeyCode.HankakuZenkaku, 0xF3, 0x29, useScanForOutput: true),
        new(KeyCode.Digit1, 0x31, 0x02),
        new(KeyCode.Digit2, 0x32, 0x03),
        new(KeyCode.Digit3, 0x33, 0x04),
        new(KeyCode.Digit4, 0x34, 0x05),
        new(KeyCode.Digit5, 0x35, 0x06),
        new(KeyCode.Digit6, 0x36, 0x07),
        new(KeyCode.Digit7, 0x37, 0x08),
        new(KeyCode.Digit8, 0x38, 0x09),
        new(KeyCode.Digit9, 0x39, 0x0A),
        new(KeyCode.Digit0, 0x30, 0x0B),
        new(KeyCode.Minus, 0xBD, 0x0C),
        new(KeyCode.Caret, 0xDE, 0x0D),
        new(KeyCode.Yen, 0xDC, 0x7D),
        new(KeyCode.Backspace, 0x08, 0x0E),

        new(KeyCode.Tab, 0x09, 0x0F),
        new(KeyCode.Q, 0x51, 0x10),
        new(KeyCode.W, 0x57, 0x11),
        new(KeyCode.E, 0x45, 0x12),
        new(KeyCode.R, 0x52, 0x13),
        new(KeyCode.T, 0x54, 0x14),
        new(KeyCode.Y, 0x59, 0x15),
        new(KeyCode.U, 0x55, 0x16),
        new(KeyCode.I, 0x49, 0x17),
        new(KeyCode.O, 0x4F, 0x18),
        new(KeyCode.P, 0x50, 0x19),
        new(KeyCode.At, 0xC0, 0x1A),
        new(KeyCode.LBracket, 0xDB, 0x1B),
        new(KeyCode.Enter, 0x0D, 0x1C),

        new(KeyCode.CapsLock, 0xF0, 0x3A, useScanForOutput: true),
        new(KeyCode.A, 0x41, 0x1E),
        new(KeyCode.S, 0x53, 0x1F),
        new(KeyCode.D, 0x44, 0x20),
        new(KeyCode.F, 0x46, 0x21),
        new(KeyCode.G, 0x47, 0x22),
        new(KeyCode.H, 0x48, 0x23),
        new(KeyCode.J, 0x4A, 0x24),
        new(KeyCode.K, 0x4B, 0x25),
        new(KeyCode.L, 0x4C, 0x26),
        new(KeyCode.SColon, 0xBB, 0x27),
        new(KeyCode.Colon, 0xBA, 0x28),
        new(KeyCode.RBracket, 0xDD, 0x2B),

        new(KeyCode.LeftShift, 0xA0, 0x2A),
        new(KeyCode.Z, 0x5A, 0x2C),
        new(KeyCode.X, 0x58, 0x2D),
        new(KeyCode.C, 0x43, 0x2E),
        new(KeyCode.V, 0x56, 0x2F),
        new(KeyCode.B, 0x42, 0x30),
        new(KeyCode.N, 0x4E, 0x31),
        new(KeyCode.M, 0x4D, 0x32),
        new(KeyCode.Comma, 0xBC, 0x33),
        new(KeyCode.Dot, 0xBE, 0x34),
        new(KeyCode.Slash, 0xBF, 0x35),
        new(KeyCode.Ro, 0xE2, 0x73),
        new(KeyCode.RightShift, 0xA1, 0x36),

        new(KeyCode.LeftCtrl, 0xA2, 0x1D),
        new(KeyCode.LeftWin, 0x5B, 0x5B, extended: true),
        new(KeyCode.LeftAlt, 0xA4, 0x38),
        new(KeyCode.NonConvert, 0x1D, 0x7B, useScanForOutput: true),
        new(KeyCode.Space, 0x20, 0x39),
        new(KeyCode.Convert, 0x1C, 0x79, useScanForOutput: true),
        new(KeyCode.Kana, 0xF2, 0x70, useScanForOutput: true),
        new(KeyCode.RightAlt, 0xA5, 0x38, extended: true),
        new(KeyCode.RightWin, 0x5C, 0x5C, extended: true),
        new(KeyCode.Apps, 0x5D, 0x5D, extended: true),
        new(KeyCode.RightCtrl, 0xA3, 0x1D, extended: true),

        new(KeyCode.Insert, 0x2D, 0x52, extended: true),
        new(KeyCode.Home, 0x24, 0x47, extended: true),
        new(KeyCode.PageUp, 0x21, 0x49, extended: true),
        new(KeyCode.Delete, 0x2E, 0x53, extended: true),
        new(KeyCode.End, 0x23, 0x4F, extended: true),
        new(KeyCode.PageDown, 0x22, 0x51, extended: true),

        new(KeyCode.Up, 0x26, 0x48, extended: true),
        new(KeyCode.Left, 0x25, 0x4B, extended: true),
        new(KeyCode.Down, 0x28, 0x50, extended: true),
        new(KeyCode.Right, 0x27, 0x4D, extended: true),

        new(KeyCode.NumLock, 0x90, 0x45, extended: true),
        new(KeyCode.NumpadDivide, 0x6F, 0x35, extended: true),
        new(KeyCode.NumpadMultiply, 0x6A, 0x37),
        new(KeyCode.NumpadSubtract, 0x6D, 0x4A),
        new(KeyCode.Numpad7, 0x67, 0x47),
        new(KeyCode.Numpad8, 0x68, 0x48),
        new(KeyCode.Numpad9, 0x69, 0x49),
        new(KeyCode.NumpadAdd, 0x6B, 0x4E),
        new(KeyCode.Numpad4, 0x64, 0x4B),
        new(KeyCode.Numpad5, 0x65, 0x4C),
        new(KeyCode.Numpad6, 0x66, 0x4D),
        new(KeyCode.Numpad1, 0x61, 0x4F),
        new(KeyCode.Numpad2, 0x62, 0x50),
        new(KeyCode.Numpad3, 0x63, 0x51),
        new(KeyCode.NumpadEnter, 0x0D, 0x1C, extended: true, useScanForOutput: true),
        new(KeyCode.Numpad0, 0x60, 0x52),
        new(KeyCode.NumpadDecimal, 0x6E, 0x53),
    ];

    private static readonly IReadOnlyDictionary<(ushort ScanCode, bool IsExtended), WindowsJis109Key> ByScan =
        Registry.ToDictionary(item => (item.ScanCode, item.IsExtended));

    private static readonly IReadOnlyDictionary<KeyCode, WindowsJis109Key> ByCode =
        Registry.ToDictionary(item => item.Code);

    static WindowsJis109KeyRegistry()
    {
        var canonical = Jis109PhysicalKeyRegistry.Keys.Select(item => item.Code).Order().ToArray();
        var windows = Registry.Select(item => item.Code).Order().ToArray();
        if (Registry.Length != 109 || !canonical.SequenceEqual(windows))
            throw new InvalidOperationException("Windows JIS109 mapping must cover the canonical 109-key surface exactly once.");
    }

    public static IReadOnlyList<WindowsJis109Key> Keys => Registry;

    public static bool TryResolveInput(KeyboardKey key, out KeyId keyId)
    {
        if (key.ScanCode != 0 && ByScan.TryGetValue((key.ScanCode, key.IsExtended), out var physical))
        {
            keyId = new KeyId(physical.Code);
            return true;
        }

        keyId = default;
        return false;
    }

    public static bool TryResolveOutput(KeyCode code, out KeyboardKey key)
    {
        if (!ByCode.TryGetValue(code, out var physical))
        {
            key = default;
            return false;
        }

        key = physical.UseScanForOutput
            ? new KeyboardKey(physical.VirtualKey, physical.ScanCode, physical.IsExtended)
            : new KeyboardKey(physical.VirtualKey, 0, physical.IsExtended);
        return true;
    }
}

internal readonly record struct WindowsJis109Key(
    KeyCode Code,
    ushort VirtualKey,
    ushort ScanCode,
    bool IsExtended = false,
    bool UseScanForOutput = false);
