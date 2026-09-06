using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyMapTests
{
    [Fact]
    public void Jis109_registry_and_Windows_mapping_are_symmetric()
    {
        Assert.Equal(109, Jis109PhysicalKeyRegistry.Keys.Count);
        Assert.Equal(109, WindowsKeyMap.Jis109PhysicalBindings.Count);

        var physicalCodes = Jis109PhysicalKeyRegistry.Keys.Select(item => item.Code).ToHashSet();
        var windowsCodes = WindowsKeyMap.Jis109PhysicalBindings.Select(item => item.Code).ToHashSet();
        Assert.True(physicalCodes.SetEquals(windowsCodes));

        foreach (var binding in WindowsKeyMap.Jis109PhysicalBindings)
        {
            var resolved = WindowsKeyMap.TryResolveKeyId(binding.WindowsKey);
            Assert.NotNull(resolved);
            Assert.True(resolved.Value.IsCompact);
            Assert.Equal(binding.Code, resolved.Value.Code);

            Assert.True(WindowsKeyMap.TryResolveOutputKey(new KeyId(binding.Code), out var output));
            Assert.Equal(binding.WindowsKey, output);
        }
    }

    [Theory]
    [InlineData((ushort)'A', KeyCode.A)]
    [InlineData((ushort)'Z', KeyCode.Z)]
    [InlineData((ushort)'0', KeyCode.Digit0)]
    [InlineData((ushort)'9', KeyCode.Digit9)]
    [InlineData(WindowsKeyMap.F1, KeyCode.F1)]
    [InlineData(WindowsKeyMap.F12, KeyCode.F12)]
    [InlineData(WindowsKeyMap.OemSemicolon, KeyCode.SColon)]
    [InlineData(WindowsKeyMap.OemPlus, KeyCode.Colon)]
    [InlineData(WindowsKeyMap.Space, KeyCode.Space)]
    [InlineData(WindowsKeyMap.Convert, KeyCode.Convert)]
    [InlineData(WindowsKeyMap.NonConvert, KeyCode.NonConvert)]
    [InlineData(WindowsKeyMap.RightWin, KeyCode.RightWin)]
    [InlineData(WindowsKeyMap.Numpad0, KeyCode.Numpad0)]
    public void Virtual_key_fallback_resolves_supported_physical_ids(ushort virtualKey, KeyCode expected)
    {
        var key = WindowsKeyMap.TryResolveKeyId(virtualKey);

        Assert.NotNull(key);
        Assert.True(key.Value.IsCompact);
        Assert.Equal(expected, key.Value.Code);
    }

    [Theory]
    [InlineData((ushort)0x70, false, KeyCode.Kana)]
    [InlineData((ushort)0x79, false, KeyCode.Convert)]
    [InlineData((ushort)0x7B, false, KeyCode.NonConvert)]
    [InlineData((ushort)0x29, false, KeyCode.HankakuZenkaku)]
    [InlineData((ushort)0x1D, false, KeyCode.LeftCtrl)]
    [InlineData((ushort)0x1D, true, KeyCode.RightCtrl)]
    public void Scan_identity_can_resolve_JIS_and_sided_keys_without_relying_on_VK(
        ushort scanCode,
        bool extended,
        KeyCode expected)
    {
        var key = WindowsKeyMap.TryResolveKeyId(new KeyboardKey(0x00, scanCode, extended));

        Assert.NotNull(key);
        Assert.Equal(expected, key.Value.Code);
    }

    [Fact]
    public void Extended_return_is_numpad_enter_while_main_return_stays_enter()
    {
        var main = WindowsKeyMap.TryResolveKeyId(new KeyboardKey(WindowsKeyMap.Enter, 0x1C, false));
        var numpad = WindowsKeyMap.TryResolveKeyId(new KeyboardKey(WindowsKeyMap.Enter, 0x1C, true));

        Assert.Equal(KeyCode.Enter, main?.Code);
        Assert.Equal(KeyCode.NumpadEnter, numpad?.Code);
    }

    [Fact]
    public void Jis_semicolon_and_colon_use_the_correct_physical_OEM_positions()
    {
        Assert.True(WindowsKeyMap.TryResolveCharacter(';', out var semicolon));
        Assert.True(WindowsKeyMap.TryResolveCharacter(':', out var colon));

        Assert.Equal((ushort)0xBB, semicolon.VirtualKey);
        Assert.Equal((ushort)0xBA, colon.VirtualKey);
        Assert.Equal(KeyCode.SColon, WindowsKeyMap.TryResolveKeyId(semicolon)?.Code);
        Assert.Equal(KeyCode.Colon, WindowsKeyMap.TryResolveKeyId(colon)?.Code);
    }

    [Fact]
    public void Unknown_physical_key_is_not_claimed()
    {
        Assert.Null(WindowsKeyMap.TryResolveKeyId(new KeyboardKey(0xFF, 0xFF, false)));
    }
}
