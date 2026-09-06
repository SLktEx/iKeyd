using Xunit;

public sealed class BehaviorIrTests
{
    [Fact]
    public void Physical_position_is_first_class_and_rejects_negative_coordinates()
    {
        var position = new KeyPosition(2, 3);

        Assert.Equal(2, position.Row);
        Assert.Equal(3, position.Column);
        Assert.Equal("POS[2,3]", position.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyPosition(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyPosition(0, -1));
    }

    [Fact]
    public void Qmk_accepts_portable_position_combo_semantics()
    {
        var document = new BehaviorIrDocument(
        [
            new ComboIr(
                [new KeyPosition(1, 1), new KeyPosition(1, 2)],
                new KeyOutputIr("ESC"))
        ]);

        var diagnostics = CompilationTargetValidator.Validate(document, CompilationTarget.Qmk);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Qmk_rejects_host_only_clipboard_behavior_with_source_location()
    {
        var source = new SourceLocation("keymap.ikeyd", 143, 9);
        var document = new BehaviorIrDocument(
        [
            new KeyBindingIr(
                new KeyPosition(3, 1),
                new ClipboardIr("history.next", source))
        ]);

        var diagnostic = Assert.Single(CompilationTargetValidator.Validate(document, CompilationTarget.Qmk));

        Assert.Equal("IKYD2041", diagnostic.Code);
        Assert.Equal(source, diagnostic.Source);
        Assert.Contains("`clipboard`", diagnostic.Message);
        Assert.Contains("`qmk`", diagnostic.Message);
    }

    [Fact]
    public void Nested_macro_reports_the_actual_unsupported_child_location()
    {
        var macroSource = new SourceLocation("keymap.ikeyd", 10, 1);
        var commandSource = new SourceLocation("keymap.ikeyd", 12, 5);
        var document = new BehaviorIrDocument(
        [
            new MacroIr(
            [
                new KeyOutputIr("A"),
                new HostCommandIr("wt.exe", commandSource)
            ],
            macroSource)
        ]);

        var diagnostic = Assert.Single(CompilationTargetValidator.Validate(document, CompilationTarget.Zmk));

        Assert.Equal(commandSource, diagnostic.Source);
        Assert.Contains("`host-command`", diagnostic.Message);
        Assert.DoesNotContain("macro", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Current_ikeyd_backend_accepts_host_integrations()
    {
        var document = new BehaviorIrDocument(
        [
            new AppContextIr(
                "process == terminal",
                new MacroIr(
                [
                    new ClipboardIr("history.next"),
                    new HostCommandIr("wt.exe")
                ]))
        ]);

        var diagnostics = CompilationTargetValidator.Validate(document, CompilationTarget.IKeydCSharp);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Backend_descriptors_separate_codegen_availability_from_semantic_capabilities()
    {
        var qmk = BackendCapabilities.Get(CompilationTarget.Qmk);
        var current = BackendCapabilities.Get(CompilationTarget.IKeydCSharp);

        Assert.False(qmk.CodegenAvailable);
        Assert.Contains(BehaviorCapability.Combo, qmk.Capabilities);
        Assert.DoesNotContain(BehaviorCapability.Clipboard, qmk.Capabilities);
        Assert.True(current.CodegenAvailable);
    }

    [Fact]
    public void Target_blocks_are_additive_leaf_metadata_and_do_not_replace_portable_bindings()
    {
        var extension = new TargetExtensionIr(
            TargetSelector.Qmk,
            [],
            [new TargetOptionIr("combo_term", "40ms")],
            []);
        var document = new BehaviorIrDocument(
        [
            new KeyBindingIr(new KeyPosition(1, 1), new KeyOutputIr("A")),
            extension
        ]);

        Assert.Empty(extension.Children);
        Assert.Contains(document.Traverse(), node => node is KeyBindingIr);
        Assert.Single(TargetExtensionSemantics.Select(document, CompilationTarget.Qmk));
        Assert.Empty(TargetExtensionSemantics.Select(document, CompilationTarget.Zmk));
    }

    [Fact]
    public void IKeyd_target_family_applies_to_both_runtime_languages()
    {
        var document = new BehaviorIrDocument(
        [
            new TargetExtensionIr(
                TargetSelector.IKeyd,
                [],
                [new TargetOptionIr("diagnostics", "verbose")],
                [])
        ]);

        Assert.Single(TargetExtensionSemantics.Select(document, CompilationTarget.IKeydCSharp));
        Assert.Single(TargetExtensionSemantics.Select(document, CompilationTarget.IKeydRust));
        Assert.Empty(TargetExtensionSemantics.Select(document, CompilationTarget.Qmk));
    }

    [Fact]
    public void Foreign_target_native_fragments_are_ignored_not_rejected()
    {
        var source = new SourceLocation("keymap.ikeyd", 20, 5);
        var document = new BehaviorIrDocument(
        [
            new TargetExtensionIr(
                TargetSelector.Qmk,
                [],
                [],
                [new NativeTargetFragmentIr("keymap.c", "// qmk only", source)],
                source)
        ]);

        Assert.Empty(CompilationTargetValidator.Validate(document, CompilationTarget.Zmk));
        Assert.Empty(CompilationTargetValidator.Validate(document, CompilationTarget.IKeydCSharp));
    }

    [Fact]
    public void Native_fragments_are_only_allowed_for_explicit_embedded_targets()
    {
        var source = new SourceLocation("keymap.ikeyd", 30, 7);
        var document = new BehaviorIrDocument(
        [
            new TargetExtensionIr(
                TargetSelector.IKeyd,
                [],
                [],
                [new NativeTargetFragmentIr("csharp", "unsafe escape", source)])
        ]);

        var diagnostic = Assert.Single(
            CompilationTargetValidator.Validate(document, CompilationTarget.IKeydCSharp));

        Assert.Equal("IKYD2042", diagnostic.Code);
        Assert.Equal(source, diagnostic.Source);
    }

    [Fact]
    public void Explicit_target_requirements_use_the_same_capability_error_contract()
    {
        var source = new SourceLocation("keymap.ikeyd", 42, 9);
        var document = new BehaviorIrDocument(
        [
            new TargetExtensionIr(
                TargetSelector.Zmk,
                [new TargetCapabilityRequirementIr(BehaviorCapability.Pointer, source)],
                [],
                [])
        ]);

        var diagnostic = Assert.Single(
            CompilationTargetValidator.Validate(document, CompilationTarget.Zmk));

        Assert.Equal("IKYD2041", diagnostic.Code);
        Assert.Equal(source, diagnostic.Source);
        Assert.Contains("`pointer`", diagnostic.Message);
        Assert.Contains("`zmk`", diagnostic.Message);
    }

    [Fact]
    public void Duplicate_options_across_matching_target_blocks_are_rejected()
    {
        var second = new SourceLocation("keymap.ikeyd", 52, 5);
        var document = new BehaviorIrDocument(
        [
            new TargetExtensionIr(
                TargetSelector.Qmk,
                [],
                [new TargetOptionIr("combo_term", "40ms")],
                []),
            new TargetExtensionIr(
                TargetSelector.Qmk,
                [],
                [new TargetOptionIr("combo_term", "50ms", second)],
                [])
        ]);

        var diagnostic = Assert.Single(
            CompilationTargetValidator.Validate(document, CompilationTarget.Qmk));

        Assert.Equal("IKYD2043", diagnostic.Code);
        Assert.Equal(second, diagnostic.Source);
    }
}
