using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class KeyboardContractsTests
{
    [Fact]
    public void Keyboard_key_keeps_virtual_scan_and_extended_identity()
    {
        var regular = new KeyboardKey(0x25, 0x4B, false);
        var extended = new KeyboardKey(0x25, 0x4B, true);

        Assert.NotEqual(regular, extended);
        Assert.Equal((ushort)0x25, regular.VirtualKey);
        Assert.Equal((ushort)0x4B, regular.ScanCode);
    }

    [Fact]
    public void Keyboard_event_carries_origin_kind_and_timestamp()
    {
        var keyboardEvent = new KeyboardEvent(
            new KeyboardKey(0x41, 0x1E),
            KeyEventKind.Down,
            KeyEventOrigin.Physical,
            1234);

        Assert.Equal(KeyEventKind.Down, keyboardEvent.Kind);
        Assert.Equal(KeyEventOrigin.Physical, keyboardEvent.Origin);
        Assert.Equal(1234, keyboardEvent.TimestampMs);
    }
}
