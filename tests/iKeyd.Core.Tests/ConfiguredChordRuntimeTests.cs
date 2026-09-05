using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Runtime;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ConfiguredChordRuntimeTests
{
    [Fact]
    public void Named_keymaps_are_selected_without_profile_specific_types()
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "alpha",
                    [new SingleMapping<string>("A", "a")],
                    [new ChordMapping<string>("A", "B", "ab")]),
                new AutomationKeymapProfile(
                    "beta",
                    [new SingleMapping<string>("A", "x")],
                    [])
            ],
            startupMode: "alpha");
        var runtime = new ConfiguredChordRuntime(profile);

        Assert.True(runtime.TryGetSingle("ALPHA", new KeyId("a"), out var alpha));
        Assert.Equal("a", alpha);
        Assert.True(runtime.TryGetSingle("beta", new KeyId("A"), out var beta));
        Assert.Equal("x", beta);

        Assert.Empty(runtime.OnKeyDown("alpha", new KeyId("A"), 0));
        Assert.Equal(ChordEngineState.PendingSingle, runtime.GetState("alpha"));
        Assert.Equal(["ab"], runtime.OnKeyDown("alpha", new KeyId("B"), 20));
        Assert.Equal(ChordEngineState.Idle, runtime.GetState("alpha"));
    }

    [Fact]
    public void Flush_all_is_profile_agnostic()
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile("one", [new SingleMapping<string>("A", "1")], []),
                new AutomationKeymapProfile("two", [new SingleMapping<string>("B", "2")], [])
            ],
            startupMode: "one");
        var runtime = new ConfiguredChordRuntime(profile);

        runtime.OnKeyDown("one", new KeyId("A"), 0);
        runtime.OnKeyDown("two", new KeyId("B"), 0);

        Assert.Equal(["1", "2"], runtime.FlushAll().OrderBy(item => item).ToArray());
    }
}
