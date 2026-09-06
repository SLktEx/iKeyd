using System.Runtime.InteropServices;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyboardOutputTests
{
    [Fact]
    public void Native_input_layout_matches_Win32_INPUT_size()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<WindowsKeyboardOutput.NativeInput>());
    }

    [Fact]
    public void Scan_code_output_uses_scan_code_and_own_injection_marker()
    {
        var input = WindowsKeyboardOutput.BuildKeyInput(new KeyboardKey(0x41, 0x1E), KeyEventKind.Down);

        Assert.Equal((ushort)0, input.Data.Keyboard.VirtualKey);
        Assert.Equal((ushort)0x1E, input.Data.Keyboard.ScanCode);
        Assert.Equal(WindowsKeyboardOutput.InjectionMarker, input.Data.Keyboard.ExtraInfo);
        Assert.NotEqual(0u, input.Data.Keyboard.Flags & 0x0008u);
    }

    [Fact]
    public void Combined_vk_sc_legacy_key_uses_the_pair_preserving_compatibility_path()
    {
        Assert.True(WindowsKeyboardOutput.UsesCombinedVirtualScanPath(new KeyboardKey(0xF3, 0x29)));
        Assert.True(WindowsKeyboardOutput.UsesCombinedVirtualScanPath(new KeyboardKey(0x1C, 0x79)));
        Assert.False(WindowsKeyboardOutput.UsesCombinedVirtualScanPath(new KeyboardKey(0x41, 0)));
        Assert.False(WindowsKeyboardOutput.UsesCombinedVirtualScanPath(new KeyboardKey(0, 0x1E)));
    }

    [Fact]
    public void Extended_key_up_sets_extended_and_keyup_flags()
    {
        var input = WindowsKeyboardOutput.BuildKeyInput(new KeyboardKey(0x25, 0x4B, true), KeyEventKind.Up);

        Assert.NotEqual(0u, input.Data.Keyboard.Flags & 0x0001u);
        Assert.NotEqual(0u, input.Data.Keyboard.Flags & 0x0002u);
    }

    [Fact]
    public void Unicode_output_uses_unicode_flag_and_utf16_code_unit()
    {
        var input = WindowsKeyboardOutput.BuildUnicodeInput('あ', KeyEventKind.Down);

        Assert.Equal((ushort)'あ', input.Data.Keyboard.ScanCode);
        Assert.NotEqual(0u, input.Data.Keyboard.Flags & 0x0004u);
        Assert.Equal(WindowsKeyboardOutput.InjectionMarker, input.Data.Keyboard.ExtraInfo);
    }

    [Fact]
    public void Supplementary_unicode_preserves_surrogate_pair_order_as_one_logical_text_output()
    {
        const string value = "🦀";
        var inputs = new WindowsKeyboardOutput.NativeInput[value.Length * 2];

        WindowsKeyboardOutput.FillUnicodeInputs(value, inputs);

        Assert.Equal(4, inputs.Length);
        Assert.Equal((ushort)value[0], inputs[0].Data.Keyboard.ScanCode);
        Assert.Equal((ushort)value[0], inputs[1].Data.Keyboard.ScanCode);
        Assert.Equal((ushort)value[1], inputs[2].Data.Keyboard.ScanCode);
        Assert.Equal((ushort)value[1], inputs[3].Data.Keyboard.ScanCode);
        Assert.NotEqual(0u, inputs[0].Data.Keyboard.Flags & 0x0004u);
        Assert.Equal(0u, inputs[0].Data.Keyboard.Flags & 0x0002u);
        Assert.NotEqual(0u, inputs[1].Data.Keyboard.Flags & 0x0002u);
        Assert.Equal(0u, inputs[2].Data.Keyboard.Flags & 0x0002u);
        Assert.NotEqual(0u, inputs[3].Data.Keyboard.Flags & 0x0002u);
    }

    [Fact]
    public void Mixed_unicode_text_preserves_utf16_code_unit_order()
    {
        const string value = "Aあ🦀";
        var inputs = new WindowsKeyboardOutput.NativeInput[value.Length * 2];

        WindowsKeyboardOutput.FillUnicodeInputs(value, inputs);

        for (var index = 0; index < value.Length; index++)
        {
            Assert.Equal((ushort)value[index], inputs[index * 2].Data.Keyboard.ScanCode);
            Assert.Equal((ushort)value[index], inputs[index * 2 + 1].Data.Keyboard.ScanCode);
        }
    }
}
