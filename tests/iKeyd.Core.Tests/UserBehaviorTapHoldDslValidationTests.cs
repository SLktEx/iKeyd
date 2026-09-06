using iKeyd.Core.Behaviors;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class UserBehaviorTapHoldDslValidationTests
{
    [Fact]
    public void Canonical_dsl_rejects_invalid_custom_tapping_term_with_source_context()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(Source("tapping_term = nope"), "bad-tap-hold.ikeyd"));

        Assert.Contains("bad-tap-hold.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains("SMART_TH", error.Message, StringComparison.Ordinal);
        Assert.Contains("tapping_term", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Canonical_dsl_rejects_unknown_custom_tap_hold_option_with_source_context()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(Source("unexpected = true"), "unknown-option.ikeyd"));

        Assert.Contains("unknown-option.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains("SMART_TH", error.Message, StringComparison.Ordinal);
        Assert.Contains("unexpected", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tap_hold_handlers_and_invocation_options_round_trip_through_optional_json()
    {
        var document = IKeydDslDocumentParser.Parse(Source("tapping_term = 125ms"), "round-trip.ikeyd");
        var serialized = AutomationProfileJson.Serialize(document.Profile);
        var parsed = AutomationProfileJson.Parse(serialized);

        var definition = Assert.Single(parsed.BehaviorDefinitions.Values);
        Assert.NotNull(definition.FindHandler("hold"));
        Assert.NotNull(definition.FindHandler("tap"));

        var invocation = Assert.Single(parsed.GetKeymap("S").BehaviorMappings).Invocation;
        Assert.Equal("125ms", invocation.Options["tapping_term"]);

        var runtime = new BehaviorRuntime(
            parsed.GetKeymap("S").BuildBehaviorBindings(parsed.BehaviorDefinitions));
        runtime.OnKeyDown("A", 0);
        Assert.Equal([BehaviorAction.SendKey("X")], runtime.OnKeyUp("A", 100).Actions);
    }

    private static string Source(string option)
        => $$"""
            behavior SMART_TH(tap_key, layer_name) {
                on_hold {
                    layer.on(layer_name)
                }

                on_tap {
                    send tap_key
                }
            }

            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = SMART_TH(X, NUM) {
                    {{option}}
                }
            }

            keymap K {
            }

            keymap NUM {
            }
            """;
}
