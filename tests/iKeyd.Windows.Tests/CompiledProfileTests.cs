using iKeyd.App;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class CompiledProfileTests
{
    [Fact]
    public void Generated_profile_matches_runtime_json_profile()
    {
        var generated = GeneratedProfile.Create();
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
        var runtime = IKeydConfiguration.Load(jsonPath);

        Assert.Equal(runtime.ChordWindowMs, generated.ChordWindowMs);
        Assert.Equal(runtime.StartupMode, generated.StartupMode);
        Assert.Equal(runtime.Profile.Hotkeys, generated.Profile.Hotkeys);
        Assert.Equal(runtime.Profile.Keymaps.Count, generated.Profile.Keymaps.Count);

        foreach (var expectedPair in runtime.Profile.Keymaps)
        {
            var expected = expectedPair.Value;
            var actual = generated.Profile.GetKeymap(expectedPair.Key);

            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.SingleMappings, actual.SingleMappings);
            Assert.Equal(expected.ChordMappings, actual.ChordMappings);
        }
    }
}
