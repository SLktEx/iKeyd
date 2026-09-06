using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

public static class WindowsKeyboardEventNormalizer
{
    public const uint LlkhfExtended = 0x01;
    public const uint LlkhfInjected = 0x10;
    public const uint LlkhfUp = 0x80;

    private const long NativeTimestampWrap = 1L << 32;
    private const long NativeTimestampHalfWrap = 1L << 31;

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

    /// <summary>
    /// Expands the 32-bit KBDLLHOOKSTRUCT.time value into the nearest
    /// Environment.TickCount64 epoch. Chord timing should reflect when Windows
    /// timestamped the input event, not when our hook callback happened to run.
    /// </summary>
    public static long ExpandNativeTimestamp(uint nativeTimestampMs, long referenceTickCount64)
    {
        var epoch = referenceTickCount64 & ~(NativeTimestampWrap - 1);
        var candidate = epoch + nativeTimestampMs;
        var delta = candidate - referenceTickCount64;

        if (delta > NativeTimestampHalfWrap)
            candidate -= NativeTimestampWrap;
        else if (delta < -NativeTimestampHalfWrap)
            candidate += NativeTimestampWrap;

        return candidate;
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
