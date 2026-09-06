using Xunit;

namespace iKeyd.Core.Tests;

public sealed class OneShotLayerDslTests
{
    [Fact]
    public void Canonical_dsl_accepts_OSL_and_static_generation_preserves_it()
    {
        var document = IKeydDslDocumentParser.Parse(
            """
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = OSL(NUM)
            }

            keymap K {
            }

            keymap NUM {
                B = "num-b"
            }
            """,
            "osl.ikeyd");

        var mapping = Assert.Single(document.Profile.GetKeymap("S").BehaviorMappings);
        Assert.Equal("OSL", mapping.Invocation.Name);
        Assert.Equal("NUM", Assert.Single(mapping.Invocation.Arguments));

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("new BehaviorInvocationProfile(\"OSL\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"NUM\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_dsl_rejects_unknown_OSL_layer_target()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(
                """
                profile demo {
                    chord_window = 40ms
                    startup_mode = S
                }

                keymap S {
                    A = OSL(MISSING)
                }

                keymap K {
                }
                """,
                "bad-osl.ikeyd"));

        Assert.Contains("bad-osl.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains("OSL", error.Message, StringComparison.Ordinal);
        Assert.Contains("MISSING", error.Message, StringComparison.Ordinal);
    }
}
