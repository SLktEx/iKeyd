using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyboardEventNormalizerTests
{
    [Fact]
    public void Physical_key_down_is_normalized()
    {
        var result = WindowsKeyboardEventNormalizer.Normalize(0x41, 0x1E, 0, 0, 123);

        Assert.Equal(new KeyboardKey(0x41, 0x1E), result.Key);
        Assert.Equal(KeyEventKind.Down, result.Kind);
        Assert.Equal(KeyEventOrigin.Physical, result.Origin);
        Assert.Equal(123, result.TimestampMs);
    }

    [Fact]
    public void Extended_key_up_is_normalized()
    {
        var flags = WindowsKeyboardEventNormalizer.LlkhfExtended | WindowsKeyboardEventNormalizer.LlkhfUp;
        var result = WindowsKeyboardEventNormalizer.Normalize(0x25, 0x4B, flags, 0, 50);

        Assert.True(result.Key.IsExtended);
        Assert.Equal(KeyEventKind.Up, result.Kind);
    }

    [Fact]
    public void Native_hook_timestamp_preserves_event_time_when_callback_runs_later()
    {
        var expanded = WindowsKeyboardEventNormalizer.ExpandNativeTimestamp(
            nativeTimestampMs: 999_900,
            referenceTickCount64: 1_000_250);

        Assert.Equal(999_900, expanded);
    }

    [Fact]
    public void Native_hook_timestamp_unwraps_to_previous_epoch_around_uint_wrap()
    {
        var justAfterWrap = (1L << 32) + 0x20;
        var expanded = WindowsKeyboardEventNormalizer.ExpandNativeTimestamp(
            nativeTimestampMs: 0xFFFF_FFF0,
            referenceTickCount64: justAfterWrap);

        Assert.Equal(0xFFFF_FFF0L, expanded);
    }

    [Fact]
    public void Native_hook_timestamp_unwraps_to_current_epoch_after_uint_wrap()
    {
        var reference = (1L << 32) + 0x50;
        var expanded = WindowsKeyboardEventNormalizer.ExpandNativeTimestamp(
            nativeTimestampMs: 0x30,
            referenceTickCount64: reference);

        Assert.Equal((1L << 32) + 0x30, expanded);
    }

    [Fact]
    public void Injected_input_is_classified_separately()
    {
        Assert.Equal(
            KeyEventOrigin.Injected,
            WindowsKeyboardEventNormalizer.ClassifyOrigin(WindowsKeyboardEventNormalizer.LlkhfInjected, 0));
    }

    [Fact]
    public void Our_SendInput_marker_takes_priority_over_the_generic_injected_flag()
    {
        Assert.NotEqual((nuint)0, WindowsKeyboardOutput.InjectionMarker);
        Assert.Equal(
            KeyEventOrigin.OwnInjected,
            WindowsKeyboardEventNormalizer.ClassifyOrigin(
                WindowsKeyboardEventNormalizer.LlkhfInjected,
                WindowsKeyboardOutput.InjectionMarker));
    }
}
