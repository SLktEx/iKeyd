using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Input;
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
    [InlineData(WindowsKeyMap.Oem102, KeyCode.Ro)]
    [InlineData(WindowsKeyMap.OemYen, KeyCode.Yen)]
    [InlineData(WindowsKeyMap.Convert, KeyCode.Henkan)]
    [InlineData(WindowsKeyMap.NonConvert, KeyCode.Muhenkan)]
    [InlineData(WindowsKeyMap.Kana, KeyCode.KatakanaHiragana)]
    public void Physical_virtual_keys_resolve_directly_to_compact_ids(ushort virtualKey, KeyCode expected)
    {
        var key = WindowsKeyMap.TryResolveKeyId(virtualKey);

        Assert.NotNull(key);
        Assert.True(key.Value.IsCompact);
        Assert.Equal(expected, key.Value.Code);
    }

    [Theory]
    [InlineData(0x29, false, KeyCode.ZenkakuHankaku)]
    [InlineData(0x70, false, KeyCode.KatakanaHiragana)]
    [InlineData(0x73, false, KeyCode.Ro)]
    [InlineData(0x79, false, KeyCode.Henkan)]
    [InlineData(0x7B, false, KeyCode.Muhenkan)]
    [InlineData(0x7D, false, KeyCode.Yen)]
    public void Japanese_physical_scan_codes_resolve_independently_of_virtual_key(
        ushort scanCode,
        bool extended,
        KeyCode expected)
    {
        var key = WindowsKeyMap.TryResolveKeyId(new KeyboardKey(0, scanCode, extended));

        Assert.NotNull(key);
        Assert.Equal(expected, key.Value.Code);
    }

    [Theory]
    [InlineData(WindowsKeyMap.Enter, 0x1C, false, KeyCode.Enter)]
    [InlineData(WindowsKeyMap.Enter, 0x1C, true, KeyCode.NumpadEnter)]
    [InlineData(WindowsKeyMap.Control, 0x1D, false, KeyCode.LeftControl)]
    [InlineData(WindowsKeyMap.Control, 0x1D, true, KeyCode.RightControl)]
    [InlineData(WindowsKeyMap.Home, 0x47, true, KeyCode.Home)]
    [InlineData(WindowsKeyMap.Numpad7, 0x47, false, KeyCode.Numpad7)]
    public void Scan_code_and_extended_flag_distinguish_shared_virtual_keys(
        ushort virtualKey,
        ushort scanCode,
        bool extended,
        KeyCode expected)
    {
        var key = WindowsKeyMap.TryResolveKeyId(new KeyboardKey(virtualKey, scanCode, extended));

        Assert.NotNull(key);
        Assert.Equal(expected, key.Value.Code);
    }
}
