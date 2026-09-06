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

        var diagnostics = TargetCapabilityValidator.Validate(document, CompilationTarget.Qmk);

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

        var diagnostic = Assert.Single(TargetCapabilityValidator.Validate(document, CompilationTarget.Qmk));

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

        var diagnostic = Assert.Single(TargetCapabilityValidator.Validate(document, CompilationTarget.Zmk));

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

        var diagnostics = TargetCapabilityValidator.Validate(document, CompilationTarget.IKeydCSharp);

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
}
