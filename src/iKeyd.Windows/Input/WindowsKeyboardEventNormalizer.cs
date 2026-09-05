using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

public static class WindowsKeyboardEventNormalizer
{
    public const uint LlkhfExtended = 0x01;
    public const uint LlkhfInjected = 0x10;
    public const uint LlkhfUp = 0x80;

    public static KeyboardEvent Normalize(
        uint virtualKey,
        uint scanCode,
        uint flags,
        nuint extraInfo,
        long timestampMs)
    {
        var key = new KeyboardKey(
            checked((ushort)virtualKey),
            checked((ushort)scanCode),
            (flags & LlkhfExtended) != 0);

        var kind = (flags & LlkhfUp) != 0 ? KeyEventKind.Up : KeyEventKind.Down;
        return new KeyboardEvent(key, kind, ClassifyOrigin(flags, extraInfo), timestampMs);
    }

    public static KeyEventOrigin ClassifyOrigin(uint flags, nuint extraInfo)
    {
        if (extraInfo == WindowsKeyboardOutput.InjectionMarker)
            return KeyEventOrigin.OwnInjected;

        return (flags & LlkhfInjected) != 0
            ? KeyEventOrigin.Injected
            : KeyEventOrigin.Physical;
    }
}
