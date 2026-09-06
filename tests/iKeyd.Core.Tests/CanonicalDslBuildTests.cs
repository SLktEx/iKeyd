using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class CanonicalDslBuildTests
{
    [Fact]
    public void Canonical_hotkeySKG_dsl_matches_legacy_json_semantics_without_a_json_build_hop()
    {
        var dslPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.ikeyd");
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");

        var document = IKeydDslDocumentParser.Parse(File.ReadAllText(dslPath), dslPath);
        var generatedFromDsl = TypedProfileCompiler.Compile(document.Profile);
        var generatedFromJson = ProfileCompiler.Compile(File.ReadAllText(jsonPath));
        var legacyMouse = MouseMotionProfileJson.Parse(File.ReadAllText(jsonPath));

        Assert.Equal(generatedFromJson, generatedFromDsl);
        Assert.Equal(legacyMouse, document.Mouse);
    }

    [Fact]
    public void Typed_dsl_frontend_lowers_positions_behaviors_custom_logic_clipboard_and_mouse_without_json()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        mouse {
            engine = virtual_stick
            update = 4ms
            response {
                press = 30ms
                release = 3ms
                curve = linear
            }
            speed {
                normal = 1500px/s
                precision = 400
                fine = 90
                fast = 3200px/s
            }
            socd = neutral
            tap_nudge = 3px
            max_catchup = 20ms
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

        var document = IKeydDslDocumentParser.Parse(source, "demo.ikeyd");
        var profile = document.Profile;
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

        Assert.Equal(4, document.Mouse.UpdateIntervalMs);
        Assert.Equal(30, document.Mouse.PressMs);
        Assert.Equal(3, document.Mouse.ReleaseMs);
        Assert.Equal("linear", document.Mouse.Curve);
        Assert.Equal(1500, document.Mouse.NormalSpeed);
        Assert.Equal(400, document.Mouse.PrecisionSpeed);
        Assert.Equal(90, document.Mouse.FineSpeed);
        Assert.Equal(3200, document.Mouse.FastSpeed);
        Assert.Equal(3, document.Mouse.TapNudgePixels);
        Assert.Equal(20, document.Mouse.MaxCatchupMs);

        var generated = TypedProfileCompiler.Compile(profile);
        var generatedMouse = TypedMouseProfileCompiler.Compile(document.Mouse);
        Assert.Contains("internal static class GeneratedProfile", generated);
        Assert.Contains("new BehaviorInvocationProfile(\"SMART_LT\"", generated);
        Assert.Contains("internal static class GeneratedMouseProfile", generatedMouse);
        Assert.Contains("        1500,", generatedMouse);
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

        var error = Assert.Throws<InvalidDataException>(() => IKeydDslDocumentParser.Parse(source, "bad.ikeyd"));

        Assert.Contains("bad.ikeyd:8", error.Message);
        Assert.Contains("column 3 is out of range", error.Message);
    }
}
