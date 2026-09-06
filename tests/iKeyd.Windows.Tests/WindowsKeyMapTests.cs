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
    [InlineData(WindowsKeyMap.JisSemicolon, KeyCode.SColon)]
    [InlineData(WindowsKeyMap.JisColon, KeyCode.Colon)]
    [InlineData(WindowsKeyMap.OemAt, KeyCode.At)]
    public void Physical_virtual_keys_resolve_directly_to_compact_ids(ushort virtualKey, KeyCode expected)
    {
        var key = WindowsKeyMap.TryResolveKeyId(virtualKey);

        Assert.NotNull(key);
        Assert.True(key.Value.IsCompact);
        Assert.Equal(expected, key.Value.Code);
    }

    [Fact]
    public void Jis_semicolon_and_colon_use_their_real_106_109_virtual_keys()
    {
        Assert.Equal((ushort)0xBB, WindowsKeyMap.JisSemicolon);
        Assert.Equal((ushort)0xBA, WindowsKeyMap.JisColon);
        Assert.Equal(WindowsKeyMap.JisSemicolon, WindowsKeyMap.OemSemicolon);
        Assert.Equal(WindowsKeyMap.JisColon, WindowsKeyMap.OemPlus);
    }

    [Theory]
    [InlineData(';', (ushort)0xBB)]
    [InlineData(':', (ushort)0xBA)]
    public void Jis_punctuation_character_output_uses_matching_physical_key(char character, ushort expectedVirtualKey)
    {
        Assert.True(WindowsKeyMap.TryResolveCharacter(character, out var key));
        Assert.Equal(expectedVirtualKey, key.VirtualKey);
    }

    [Fact]
    public void Entire_number_row_resolves_to_distinct_compact_ids()
    {
        for (var digit = 0; digit <= 9; digit++)
        {
            var virtualKey = (ushort)('0' + digit);
            var key = WindowsKeyMap.TryResolveKeyId(virtualKey);

            Assert.NotNull(key);
            Assert.Equal((KeyCode)((int)KeyCode.Digit0 + digit), key.Value.Code);
        }
    }

    [Fact]
    public void Entire_function_row_resolves_to_distinct_compact_ids()
    {
        for (var functionNumber = 1; functionNumber <= 12; functionNumber++)
        {
            var virtualKey = (ushort)(WindowsKeyMap.F1 + functionNumber - 1);
            var key = WindowsKeyMap.TryResolveKeyId(virtualKey);

            Assert.NotNull(key);
            Assert.Equal((KeyCode)((int)KeyCode.F1 + functionNumber - 1), key.Value.Code);
        }
    }

    [Theory]
    [InlineData(WindowsKeyMap.Kana)]
    [InlineData(WindowsKeyMap.Alt)]
    [InlineData(WindowsKeyMap.Convert)]
    [InlineData(WindowsKeyMap.NonConvert)]
    [InlineData(WindowsKeyMap.Space)]
    public void Layer_and_modifier_virtual_keys_are_not_character_key_ids(ushort virtualKey)
    {
        var key = WindowsKeyMap.TryResolveKeyId(virtualKey);

        Assert.Null(key);
    }
}
