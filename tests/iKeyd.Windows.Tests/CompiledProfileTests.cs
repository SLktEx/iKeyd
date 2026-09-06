using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Keymaps;
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
        Assert.Equal(runtime.Mouse, GeneratedMouseProfile.Create());

        foreach (var expectedPair in runtime.Profile.Keymaps)
        {
            var expected = expectedPair.Value;
            var actual = generated.Profile.GetKeymap(expectedPair.Key);

            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.SingleMappings, actual.SingleMappings);
            Assert.Equal(expected.ChordMappings, actual.ChordMappings);
        }

        AssertLookupEquivalent(runtime.SKeymap, generated.SKeymap);
        AssertLookupEquivalent(runtime.KKeymap, generated.KKeymap);
    }

    private static void AssertLookupEquivalent(Keymap<string> expected, Keymap<string> actual)
    {
        for (var firstCode = (int)KeyCode.A; firstCode <= (int)KeyCode.At; firstCode++)
        {
            var first = new KeyId((KeyCode)firstCode);
            var expectedSingleFound = expected.TryGetSingle(first, out var expectedSingle);
            var actualSingleFound = actual.TryGetSingle(first, out var actualSingle);
            Assert.Equal(expectedSingleFound, actualSingleFound);
            if (expectedSingleFound)
                Assert.Equal(expectedSingle, actualSingle);

            for (var secondCode = (int)KeyCode.A; secondCode <= (int)KeyCode.At; secondCode++)
            {
                var second = new KeyId((KeyCode)secondCode);
                var expectedChordFound = expected.TryGetChord(first, second, out var expectedChord);
                var actualChordFound = actual.TryGetChord(first, second, out var actualChord);
                Assert.Equal(expectedChordFound, actualChordFound);
                if (expectedChordFound)
                    Assert.Equal(expectedChord, actualChord);
            }
        }
    }
}
