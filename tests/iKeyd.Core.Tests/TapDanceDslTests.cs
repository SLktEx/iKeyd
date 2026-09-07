using Xunit;

namespace iKeyd.Core.Tests;

public sealed class TapDanceDslTests
{
    [Fact]
    public void Canonical_dsl_accepts_TD_and_static_generation_preserves_it()
    {
        var document = IKeydDslDocumentParser.Parse(
            """
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = TD(X, Y, Z) {
                    tapping_term = 175ms
                }
            }

            keymap K {
            }
            """,
            "tap-dance.ikeyd");

        var mapping = Assert.Single(document.Profile.GetKeymap("S").BehaviorMappings);
        Assert.Equal("TD", mapping.Invocation.Name);
        Assert.Equal(["X", "Y", "Z"], mapping.Invocation.Arguments);
        Assert.Equal("175ms", mapping.Invocation.Options["tapping_term"]);

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("new BehaviorInvocationProfile(\"TD\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"X\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"Y\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"Z\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"tapping_term\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"175ms\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_dsl_rejects_single_output_TD_during_parse()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(
                """
                profile demo {
                    chord_window = 40ms
                    startup_mode = S
                }

                keymap S {
                    A = TD(X)
                }

                keymap K {
                }
                """,
                "bad-td-count.ikeyd"));

        Assert.Contains("bad-td-count.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains("TD", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_dsl_rejects_more_than_eight_outputs_during_parse()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(
                """
                profile demo {
                    chord_window = 40ms
                    startup_mode = S
                }

                keymap S {
                    A = TD(A, B, C, D, E, F, G, H, I)
                }

                keymap K {
                }
                """,
                "bad-td-max.ikeyd"));

        Assert.Contains("bad-td-max.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains("8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_dsl_rejects_invalid_tapping_term_during_parse()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(
                """
                profile demo {
                    chord_window = 40ms
                    startup_mode = S
                }

                keymap S {
                    A = TD(X, Y) {
                        tapping_term = forever
                    }
                }

                keymap K {
                }
                """,
                "bad-td-term.ikeyd"));

        Assert.Contains("bad-td-term.ikeyd", error.Message, StringComparison.Ordinal);
        Assert.Contains("tapping_term", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
