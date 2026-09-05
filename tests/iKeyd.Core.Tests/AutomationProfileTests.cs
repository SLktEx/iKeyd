using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class AutomationProfileTests
{
    [Fact]
    public void Profile_parser_accepts_arbitrary_named_keymaps_and_hotkeys()
    {
        const string json = """
        {
          "source": { "chordWindowMs": 55 },
          "startupMode": "Custom",
          "singleStroke": {
            "Custom": { "A": "alpha", "B": "beta" },
            "Nav": { "H": "left" }
          },
          "chords": {
            "Custom": [["A", "B", "ab"]],
            "Nav": []
          },
          "hotkeys": [
            { "trigger": "^j", "action": "Send, hello" }
          ]
        }
        """;

        var profile = AutomationProfileJson.Parse(json);

        Assert.Equal(55, profile.ChordWindowMs);
        Assert.Equal("Custom", profile.StartupMode);
        Assert.Equal(2, profile.Keymaps.Count);
        Assert.Single(profile.Hotkeys);
        Assert.Equal("^j", profile.Hotkeys[0].Trigger);

        var custom = profile.GetKeymap("custom").BuildKeymap();
        Assert.True(custom.TryGetSingle(new KeyId("a"), out var single));
        Assert.Equal("alpha", single);
        Assert.True(custom.TryGetChord(new KeyId("A"), new KeyId("B"), out var chord));
        Assert.Equal("ab", chord);
    }

    [Fact]
    public void Profile_round_trip_preserves_effective_mappings_and_chord_order()
    {
        var profile = new AutomationProfile(
            40,
            [
                new AutomationKeymapProfile(
                    "S",
                    [
                        new SingleMapping<string>("Q", "old"),
                        new SingleMapping<string>("Q", "new")
                    ],
                    [
                        new ChordMapping<string>("K", "Q", "first"),
                        new ChordMapping<string>("Q", "K", "second")
                    ])
            ],
            hotkeys: [new HotkeyBinding("F1", "Send, help")]);

        var parsed = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(profile));
        var keymap = parsed.GetKeymap("S").BuildKeymap();

        Assert.True(keymap.TryGetSingle("Q", out var single));
        Assert.Equal("new", single);
        Assert.True(keymap.TryGetChord("K", "Q", out var chord));
        Assert.Equal("first", chord);
        Assert.Equal(2, parsed.GetKeymap("S").ChordMappings.Count);
        Assert.Equal("Send, help", parsed.Hotkeys[0].Action);
    }

    [Fact]
    public void Profile_requires_matching_single_and_chord_sections()
    {
        const string json = """
        {
          "singleStroke": { "S": { "Q": "-" } },
          "chords": { "K": [] }
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => AutomationProfileJson.Parse(json));
        Assert.Contains("missing", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
