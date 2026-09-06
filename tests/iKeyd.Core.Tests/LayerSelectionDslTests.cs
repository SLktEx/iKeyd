using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class LayerSelectionDslTests
{
    [Fact]
    public void Canonical_dsl_accepts_TG_and_TO_as_first_class_helpers()
    {
        var document = IKeydDslDocumentParser.Parse(
            """
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = TG(NAV)
                B = TO(NUM)
            }

            keymap K {
            }

            keymap NAV {
                A = TG(NAV)
            }

            keymap NUM {
            }
            """,
            "layers.ikeyd");

        var mappings = document.Profile.GetKeymap("S").BehaviorMappings;
        Assert.Equal(2, mappings.Count);
        Assert.Equal("TG", mappings[0].Invocation.Name);
        Assert.Equal("NAV", mappings[0].Invocation.Arguments.Single());
        Assert.Equal("TO", mappings[1].Invocation.Name);
        Assert.Equal("NUM", mappings[1].Invocation.Arguments.Single());

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("new BehaviorInvocationProfile(\"TG\"", generated, StringComparison.Ordinal);
        Assert.Contains("new BehaviorInvocationProfile(\"TO\"", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TG")]
    [InlineData("TO")]
    public void Canonical_dsl_rejects_invalid_persistent_layer_helper_arity(string helper)
    {
        var source = $$"""
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = {{helper}}()
            }

            keymap K {
            }
            """;

        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(source, "invalid-layers.ikeyd"));

        Assert.Contains("invalid-layers.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains(helper, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("LT(MISSING, Z)")]
    [InlineData("MO(MISSING)")]
    [InlineData("TG(MISSING)")]
    [InlineData("TO(MISSING)")]
    public void Canonical_dsl_rejects_unknown_standard_layer_targets(string invocation)
    {
        var source = $$"""
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = {{invocation}}
            }

            keymap K {
            }
            """;

        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(source, "unknown-layer.ikeyd"));

        Assert.Contains("unknown-layer.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains("MISSING", error.Message, StringComparison.Ordinal);
    }
}
