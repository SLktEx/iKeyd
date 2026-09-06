using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class CanonicalDslBuildTests
{
    [Fact]
    public void Canonical_hotkeySKG_dsl_generates_the_same_static_profile_as_legacy_json()
    {
        var dslPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.ikeyd");
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

        var typedProfile = IKeydDslParser.Parse(File.ReadAllText(dslPath), dslPath);
        var generatedFromDsl = TypedProfileCompiler.Compile(typedProfile);
        var generatedFromJson = ProfileCompiler.Compile(File.ReadAllText(jsonPath));

        Assert.Equal(generatedFromJson, generatedFromDsl);
    }

    [Fact]
    public void Typed_dsl_frontend_lowers_positions_behaviors_custom_logic_and_clipboard_without_json()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        layout BASE {
            row Q W E
            row A S D
        }

        behavior SMART_LT(layer, tap) {
            var active: bool = false

            on_press {
                active = true
                layer.on(layer)
            }

            on_release {
                if active {
                    layer.off(layer)
                }
                send tap
            }
        }

        keymap S {
            POS[1,1] = SMART_LT(NUM, Z) {
                tapping_term = 170ms
                hold_on_other_key_press = false
            }
            POS[1,2] = "w"
            combo POS[1,1] + POS[2,2] = "escape"
        }

        keymap K {
            Q = "q"
            W = "w"
        }

        clipboard {
            history = true
            max_items = 100
            persist = false
            images = true
            encryption = user
            cipher = chacha20_poly1305
        }
        """;

        var profile = IKeydDslParser.Parse(source, "demo.ikeyd");
        var s = profile.GetKeymap("S");

        Assert.Equal(40, profile.ChordWindowMs);
        Assert.Equal("S", profile.StartupMode);
        Assert.Contains(s.SingleMappings, mapping => mapping.Key.Value == "W" && mapping.Output == "w");
        Assert.Contains(s.ChordMappings, mapping =>
            mapping.First.Value == "Q" && mapping.Second.Value == "S" && mapping.Output == "escape");

        var behavior = Assert.Single(s.BehaviorMappings);
        Assert.Equal("Q", behavior.Key.Value);
        Assert.Equal("SMART_LT", behavior.Invocation.Name);
        Assert.Equal(["NUM", "Z"], behavior.Invocation.Arguments);
        Assert.Equal("170ms", behavior.Invocation.Options["tapping_term"]);
        Assert.Equal("false", behavior.Invocation.Options["hold_on_other_key_press"]);

        var definition = Assert.Single(profile.BehaviorDefinitions.Values);
        Assert.Equal("SMART_LT", definition.Name);
        Assert.Equal(["layer", "tap"], definition.Parameters);
        Assert.Equal(2, definition.Handlers.Count);

        Assert.True(profile.Clipboard.History);
        Assert.Equal(100, profile.Clipboard.MaxItems);
        Assert.False(profile.Clipboard.Persist);
        Assert.True(profile.Clipboard.Images);
        Assert.Equal("chacha20-poly1305", profile.Clipboard.Cipher);

        var generated = TypedProfileCompiler.Compile(profile);
        Assert.Contains("internal static class GeneratedProfile", generated);
        Assert.Contains("new BehaviorInvocationProfile(\"SMART_LT\"", generated);
    }

    [Fact]
    public void Typed_dsl_frontend_reports_source_location_for_invalid_position()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
        }
        layout BASE {
            row Q W
        }
        keymap S {
            POS[1,3] = "x"
        }
        keymap K {
            Q = "q"
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => IKeydDslParser.Parse(source, "bad.ikeyd"));

        Assert.Contains("bad.ikeyd:8", error.Message);
        Assert.Contains("column 3 is out of range", error.Message);
    }
}
