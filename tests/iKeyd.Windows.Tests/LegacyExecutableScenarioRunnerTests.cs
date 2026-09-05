using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyExecutableScenarioRunnerTests
{
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Fact]
    public void Reference_binary_hash_is_pinned()
    {
        Assert.Equal(
            "5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea",
            LegacyExecutableScenarioRunner.ReferenceSha256);
    }

    [Theory]
    [Trait("Category", "LegacyExeE2E")]
    [InlineData("s-chord-k-q-immediate")]
    [InlineData("s-k-q-after-100ms")]
    public async Task Realtime_safe_scenarios_match_legacy_executable_when_configured(string scenarioId)
    {
        var runner = new LegacyExecutableScenarioRunner();
        if (!runner.IsAvailable)
            return;

        var scenario = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Single(item => item.Id == scenarioId);

        var result = await runner.RunAsync(scenario);
        var differences = CompatibilityScenarioDiff.Compare(scenario, result);

        Assert.Empty(differences);
        Assert.Equal("hotkeySKG.exe", result.Runner);
        Assert.Equal("legacy-process-hook-output", result.Metadata["scope"]);
    }
}
