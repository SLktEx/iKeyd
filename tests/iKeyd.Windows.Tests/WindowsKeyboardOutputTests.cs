using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyboardOutputTests
{
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
}
