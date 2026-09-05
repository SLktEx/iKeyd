using iKeyd.App;
using iKeyd.Core.Chords;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyMapTests
{
    [Theory]
    [InlineData((ushort)'A', KeyCode.A)]
    [InlineData((ushort)'Z', KeyCode.Z)]
    [InlineData((ushort)'0', KeyCode.Digit0)]
    [InlineData((ushort)'9', KeyCode.Digit9)]
    [InlineData(WindowsKeyMap.F1, KeyCode.F1)]
    [InlineData(WindowsKeyMap.F12, KeyCode.F12)]
    [InlineData(WindowsKeyMap.OemSemicolon, KeyCode.SColon)]
    [InlineData(WindowsKeyMap.OemAt, KeyCode.At)]
    public void Physical_virtual_keys_resolve_directly_to_compact_ids(ushort virtualKey, KeyCode expected)
    {
        var key = WindowsKeyMap.TryResolveKeyId(virtualKey);

        Assert.NotNull(key);
        Assert.True(key.Value.IsCompact);
        Assert.Equal(expected, key.Value.Code);
    }
}
