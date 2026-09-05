using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ScenarioKeyboardTests
{
    [Theory]
    [InlineData("KANA", 0x70)]
    [InlineData("CONVERT", 0x79)]
    [InlineData("NONCONVERT", 0x7B)]
    [InlineData("A", 0x00)]
    public void LegacyPhysicalKeysUsePinnedScanCodes(string key, byte expected)
        => Assert.Equal(expected, ScenarioKeyboard.ResolveScanCode(key));

    [Theory]
    [InlineData(0x10, "SHIFT")]
    [InlineData(0xA0, "SHIFT")]
    [InlineData(0xA1, "SHIFT")]
    [InlineData(0x11, "CTRL")]
    [InlineData(0xA2, "CTRL")]
    [InlineData(0xA3, "CTRL")]
    [InlineData(0x12, "ALT")]
    [InlineData(0xA4, "ALT")]
    [InlineData(0xA5, "ALT")]
    public void LegacyLeftRightModifiersNormalizeToSemanticModifier(ushort virtualKey, string expected)
        => Assert.Equal(expected, ScenarioKeyboard.ResolveName(virtualKey));
}
