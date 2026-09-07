using Xunit;

namespace iKeyd.Core.Tests;

public sealed class OneShotModifierDslTests
{
    [Fact]
    public void Canonical_dsl_accepts_OSM_and_static_generation_preserves_it()
    {
        var document = IKeydDslDocumentParser.Parse(
            """
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = OSM(Ctrl)
            }

            keymap K {
            }
            """,
            "osm.ikeyd");

        var mapping = Assert.Single(document.Profile.GetKeymap("S").BehaviorMappings);
        Assert.Equal("OSM", mapping.Invocation.Name);
        Assert.Equal("Ctrl", Assert.Single(mapping.Invocation.Arguments));

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("new BehaviorInvocationProfile(\"OSM\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"Ctrl\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_dsl_rejects_invalid_OSM_modifier_when_profile_is_materialized()
    {
        var document = IKeydDslDocumentParser.Parse(
            """
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = OSM(Hyper)
            }

            keymap K {
            }
            """,
            "bad-osm.ikeyd");

        Assert.Throws<InvalidDataException>(() =>
            document.Profile.GetKeymap("S").BuildBehaviorBindings(document.Profile.BehaviorDefinitions));
    }
}
