using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacySendEventScenarioRunnerLayerTests
{
    [Theory]
    [InlineData("M")]
    [InlineData("H")]
    [InlineData("S")]
    [InlineData("K")]
    [InlineData("A")]
    [InlineData("m")]
    [InlineData(" a ")]
    public void Hosted_runner_accepts_legacy_layer_tokens(string layer)
        => Assert.True(LegacySendEventScenarioRunner.IsSupportedInitialLayer(layer));

    [Theory]
    [InlineData("K")]
    [InlineData("A")]
    public void K_and_A_are_sticky_legacy_state_tokens(string layer)
    {
        Assert.True(LegacySendEventScenarioRunner.IsStickyLayer(layer));
        Assert.False(LegacySendEventScenarioRunner.IsHeldLayer(layer));
    }

    [Theory]
    [InlineData("M")]
    [InlineData("H")]
    [InlineData("S")]
    public void M_H_S_are_physically_held_until_cleanup(string layer)
    {
        Assert.True(LegacySendEventScenarioRunner.IsHeldLayer(layer));
        Assert.False(LegacySendEventScenarioRunner.IsStickyLayer(layer));
    }

    [Fact]
    public void Unknown_layer_is_rejected()
        => Assert.False(LegacySendEventScenarioRunner.IsSupportedInitialLayer("X"));
}
