using iKeyd.Core.Behaviors;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class UnicodeTextDslTests
{
    [Fact]
    public void Canonical_dsl_preserves_unicode_and_text_as_distinct_behavior_semantics()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        keymap S {
            A = UNICODE() {
                value = "🦀"
            }
            B = TEXT() {
                value = "hello 世界"
            }
        }

        keymap K {
            A = "a"
        }
        """;

        var document = IKeydDslDocumentParser.Parse(source, "unicode-text.ikeyd");
        var mappings = document.Profile.GetKeymap("S").BehaviorMappings;

        Assert.Equal(2, mappings.Count);
        Assert.Equal("UNICODE", mappings[0].Invocation.Name);
        Assert.Equal("🦀", mappings[0].Invocation.Options["value"]);
        Assert.Equal("TEXT", mappings[1].Invocation.Name);
        Assert.Equal("hello 世界", mappings[1].Invocation.Options["value"]);

        var unicode = BehaviorDefinitionFactory.Create(mappings[0].Invocation);
        var text = BehaviorDefinitionFactory.Create(mappings[1].Invocation);
        Assert.NotNull(unicode);
        Assert.NotNull(text);

        var generated = TypedProfileCompiler.Compile(document.Profile);
        Assert.Contains("new BehaviorInvocationProfile(\"UNICODE\"", generated);
        Assert.Contains("new BehaviorInvocationProfile(\"TEXT\"", generated);
        Assert.Contains("🦀", generated);
        Assert.Contains("hello 世界", generated);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("🦀x")]
    public void Canonical_dsl_rejects_multi_scalar_unicode_during_parse(string value)
    {
        var source = $$"""
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        keymap S {
            A = UNICODE() {
                value = {{System.Text.Json.JsonSerializer.Serialize(value)}}
            }
        }

        keymap K {
            A = "a"
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() =>
            IKeydDslDocumentParser.Parse(source, "invalid-literal.ikeyd"));

        Assert.Contains("invalid behavior", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("UNICODE")]
    [InlineData("TEXT")]
    public void Existing_option_grammar_rejects_empty_literal_value(string helper)
    {
        var source = $$"""
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        keymap S {
            A = {{helper}}() {
                value = ""
            }
        }

        keymap K {
            A = "a"
        }
        """;

        Assert.Throws<ArgumentException>(() =>
            IKeydDslDocumentParser.Parse(source, "empty-literal.ikeyd"));
    }
}
