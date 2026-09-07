using System.Runtime.InteropServices;
using iKeyd.App;
using iKeyd.Core.Chords;
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

    [Theory]
    [InlineData((ushort)'1', (ushort)0x02)]
    [InlineData((ushort)'9', (ushort)0x0A)]
    [InlineData((ushort)'0', (ushort)0x0B)]
    [InlineData((ushort)0x70, (ushort)0x3B)]
    [InlineData((ushort)0x79, (ushort)0x44)]
    [InlineData((ushort)0x7A, (ushort)0x57)]
    [InlineData((ushort)0x7B, (ushort)0x58)]
    public void Number_and_function_identity_replay_uses_physical_scan_code(ushort virtualKey, ushort expectedScanCode)
    {
        var normalized = WindowsKeyboardOutput.NormalizeIdentityReplayKey(new KeyboardKey(virtualKey, 0));
        var input = WindowsKeyboardOutput.BuildKeyInput(normalized, KeyEventKind.Down);

        Assert.Equal(new KeyboardKey(0, expectedScanCode), normalized);
        Assert.Equal((ushort)0, input.Data.Keyboard.VirtualKey);
        Assert.Equal(expectedScanCode, input.Data.Keyboard.ScanCode);
        Assert.NotEqual(0u, input.Data.Keyboard.Flags & 0x0008u);
        Assert.Equal(WindowsKeyboardOutput.InjectionMarker, input.Data.Keyboard.ExtraInfo);
    }

    [Fact]
    public void Number_and_function_identity_replay_matches_the_JIS109_registry()
    {
        var checkedBindings = 0;
        foreach (var binding in WindowsKeyMap.Jis109PhysicalBindings)
        {
            var isNumberRow = binding.Code is >= KeyCode.Digit0 and <= KeyCode.Digit9;
            var isFunctionRow = binding.Code is >= KeyCode.F1 and <= KeyCode.F12;
            if (!isNumberRow && !isFunctionRow)
                continue;

            var normalized = WindowsKeyboardOutput.NormalizeIdentityReplayKey(
                new KeyboardKey(binding.WindowsKey.VirtualKey, 0, binding.WindowsKey.IsExtended));

            Assert.Equal((ushort)0, normalized.VirtualKey);
            Assert.Equal(binding.WindowsKey.ScanCode, normalized.ScanCode);
            Assert.Equal(binding.WindowsKey.IsExtended, normalized.IsExtended);
            checkedBindings++;
        }

        Assert.Equal(22, checkedBindings);
    }

    [Fact]
    public void Identity_replay_normalization_does_not_change_new_shita_letter_output_or_explicit_scan_input()
    {
        var letter = new KeyboardKey((ushort)'F', 0);
        var explicitPhysical = new KeyboardKey((ushort)'1', 0x02);

        Assert.Equal(letter, WindowsKeyboardOutput.NormalizeIdentityReplayKey(letter));
        Assert.Equal(explicitPhysical, WindowsKeyboardOutput.NormalizeIdentityReplayKey(explicitPhysical));
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
