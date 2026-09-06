using Xunit;

namespace iKeyd.Core.Tests;

public sealed class TargetExtensionDslTests
{
    [Fact]
    public void Target_blocks_are_extracted_without_affecting_the_runtime_profile()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
            startup_mode = S
        }

        target qmk {
            require combo
            option combo_term = 40ms
            native keymap.c = "#define COMBO_TERM 40\n"
        }

        target zmk {
            require layer-tap
            option tapping-term-ms = 175
        }

        keymap S {
            Q = "q"
        }

        keymap K {
            Q = "q"
        }
        """;

        var document = IKeydDslDocumentParser.Parse(source, "target.ikeyd");

        Assert.Equal(2, document.TargetExtensions.Count);
        Assert.Single(document.Profile.GetKeymap("S").SingleMappings);
        Assert.Single(document.Profile.GetKeymap("K").SingleMappings);

        var qmk = Assert.Single(document.TargetExtensions.Where(item => item.Selector == TargetSelector.Qmk));
        Assert.Equal(BehaviorCapability.Combo, Assert.Single(qmk.Requirements).Capability);
        Assert.Equal("40ms", Assert.Single(qmk.Options).Value);
        var native = Assert.Single(qmk.NativeFragments);
        Assert.Equal("keymap.c", native.Kind);
        Assert.Equal("#define COMBO_TERM 40\n", native.Payload);

        var zmk = Assert.Single(document.TargetExtensions.Where(item => item.Selector == TargetSelector.Zmk));
        Assert.Equal(BehaviorCapability.LayerTap, Assert.Single(zmk.Requirements).Capability);
        Assert.Equal("175", Assert.Single(zmk.Options).Value);
    }

    [Fact]
    public void Target_blocks_cannot_override_portable_bindings()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
        }
        target qmk {
            Q = "different"
        }
        keymap S {
            Q = "q"
        }
        keymap K {
            Q = "q"
        }
        """;

        var error = Assert.Throws<InvalidDataException>(
            () => IKeydDslDocumentParser.Parse(source, "override.ikeyd"));

        Assert.Contains("portable bindings cannot be overridden", error.Message);
        Assert.Contains("override.ikeyd:5", error.Message);
    }

    [Fact]
    public void Windows_is_not_a_backend_target_selector()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
        }
        target windows {
            option anything = true
        }
        keymap S {
            Q = "q"
        }
        keymap K {
            Q = "q"
        }
        """;

        var error = Assert.Throws<InvalidDataException>(
            () => IKeydDslDocumentParser.Parse(source, "windows.ikeyd"));

        Assert.Contains("host platform, not a compiler backend target", error.Message);
        Assert.Contains("target ikeyd", error.Message);
    }

    [Fact]
    public void Native_fragment_braces_inside_a_string_do_not_break_target_block_parsing()
    {
        const string source = """
        profile demo {
            chord_window = 40ms
        }
        target qmk {
            native keymap.c = "void f(void) { if (1) { return; } }"
        }
        keymap S {
            Q = "q"
        }
        keymap K {
            Q = "q"
        }
        """;

        var document = IKeydDslDocumentParser.Parse(source, "native.ikeyd");
        var fragment = Assert.Single(Assert.Single(document.TargetExtensions).NativeFragments);

        Assert.Equal("void f(void) { if (1) { return; } }", fragment.Payload);
    }
}
