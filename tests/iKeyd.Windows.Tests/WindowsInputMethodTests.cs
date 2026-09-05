using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsInputMethodTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(19)]
    [InlineData(25)]
    [InlineData(27)]
    [InlineData(16)]
    public void Legacy_roma_kana_conversion_modes_are_active(int conversionMode)
        => Assert.True(WindowsInputMethod.IsRomaKanaConversionMode(conversionMode));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(31)]
    public void Other_conversion_modes_are_inactive(int conversionMode)
        => Assert.False(WindowsInputMethod.IsRomaKanaConversionMode(conversionMode));
}
