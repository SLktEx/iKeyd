using iKeyd.Core.Behaviors;
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
    public void Profile_parser_lowers_LT_behavior_invocation_to_generic_runtime()
    {
        const string json = """
        {
          "source": { "chordWindowMs": 40 },
          "startupMode": "Base",
          "singleStroke": { "Base": { "W": "w" } },
          "chords": { "Base": [] },
          "behaviors": {
            "Base": {
              "Q": { "name": "LT", "arguments": ["NUM", "Z"] }
            }
          }
        }
        """;

        var profile = AutomationProfileJson.Parse(json);
        var keymap = profile.GetKeymap("Base");
        var bindings = keymap.BuildBehaviorBindings();
        var runtime = new BehaviorRuntime(bindings);

        Assert.Single(keymap.BehaviorMappings);
        Assert.True(runtime.IsBound("Q"));

        var down = runtime.OnKeyDown("Q", 0);
        var up = runtime.OnKeyUp("Q", 100);

        Assert.True(down.Suppress);
        Assert.Equal([BehaviorAction.SendKey("Z")], up.Actions);
    }

    [Fact]
    public void Profile_parser_accepts_scalar_behavior_options()
    {
        const string json = """
        {
          "singleStroke": { "S": {}, "K": {} },
          "chords": { "S": [], "K": [] },
          "behaviors": {
            "S": {
              "A": {
                "name": "LT",
                "arguments": ["NUM", "Z"],
                "options": {
                  "tapping_term": "170ms",
                  "hold_on_other_key_press": false
                }
              }
            }
          }
        }
        """;

        var invocation = AutomationProfileJson.Parse(json)
            .GetKeymap("S")
            .BehaviorMappings[0]
            .Invocation;

        Assert.Equal("170ms", invocation.Options["tapping_term"]);
        Assert.Equal("false", invocation.Options["hold_on_other_key_press"]);
    }

    [Fact]
    public void Profile_round_trip_preserves_effective_mappings_chord_order_and_behaviors()
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
                    ],
                    [
                        new BehaviorMappingProfile(
                            "A",
                            new BehaviorInvocationProfile(
                                "LT",
                                ["NUM", "Z"],
                                new Dictionary<string, string>
                                {
                                    ["tapping_term"] = "170ms",
                                    ["hold_on_other_key_press"] = "false"
                                }))
                    ])
            ],
            hotkeys: [new HotkeyBinding("F1", "Send, help")]);

        var parsed = AutomationProfileJson.Parse(AutomationProfileJson.Serialize(profile));
        var parsedKeymap = parsed.GetKeymap("S");
        var keymap = parsedKeymap.BuildKeymap();

        Assert.True(keymap.TryGetSingle("Q", out var single));
        Assert.Equal("new", single);
        Assert.True(keymap.TryGetChord("K", "Q", out var chord));
        Assert.Equal("first", chord);
        Assert.Equal(2, parsedKeymap.ChordMappings.Count);
        Assert.Single(parsedKeymap.BehaviorMappings);
        Assert.Equal("LT", parsedKeymap.BehaviorMappings[0].Invocation.Name);
        Assert.Equal(["NUM", "Z"], parsedKeymap.BehaviorMappings[0].Invocation.Arguments);
        Assert.Equal("170ms", parsedKeymap.BehaviorMappings[0].Invocation.Options["tapping_term"]);
        Assert.Equal("false", parsedKeymap.BehaviorMappings[0].Invocation.Options["hold_on_other_key_press"]);
        Assert.Equal("Send, help", parsed.Hotkeys[0].Action);
    }

    [Fact]
    public void Keymap_rejects_a_string_and_behavior_mapping_on_the_same_key()
    {
        var error = Assert.Throws<ArgumentException>(() => new AutomationKeymapProfile(
            "S",
            [new SingleMapping<string>("Q", "q")],
            [],
            [new BehaviorMappingProfile("Q", new BehaviorInvocationProfile("LT", ["NUM", "Z"]))]));

        Assert.Contains("both", error.Message, StringComparison.OrdinalIgnoreCase);
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
