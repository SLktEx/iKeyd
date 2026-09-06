internal enum TargetSelector
{
    IKeyd,
    IKeydCSharp,
    IKeydRust,
    Qmk,
    Zmk,
}

internal static class TargetSelectors
{
    public static bool Matches(TargetSelector selector, CompilationTarget target) => selector switch
    {
        TargetSelector.IKeyd => target is CompilationTarget.IKeydCSharp or CompilationTarget.IKeydRust,
        TargetSelector.IKeydCSharp => target == CompilationTarget.IKeydCSharp,
        TargetSelector.IKeydRust => target == CompilationTarget.IKeydRust,
        TargetSelector.Qmk => target == CompilationTarget.Qmk,
        TargetSelector.Zmk => target == CompilationTarget.Zmk,
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown target selector."),
    };

    public static string Name(TargetSelector selector) => selector switch
    {
        TargetSelector.IKeyd => "ikeyd",
        TargetSelector.IKeydCSharp => "ikeyd-csharp",
        TargetSelector.IKeydRust => "ikeyd-rust",
        TargetSelector.Qmk => "qmk",
        TargetSelector.Zmk => "zmk",
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown target selector."),
    };
}

/// <summary>
/// Backend-only metadata. It is deliberately a leaf node: target blocks may add
/// backend options/native declarations, but may not redefine portable bindings.
/// </summary>
internal sealed record TargetExtensionIr(
    TargetSelector Selector,
    IReadOnlyList<TargetCapabilityRequirementIr> Requirements,
    IReadOnlyList<TargetOptionIr> Options,
    IReadOnlyList<NativeTargetFragmentIr> NativeFragments,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
        => Array.Empty<BehaviorCapability>();
}

internal sealed record TargetCapabilityRequirementIr(
    BehaviorCapability Capability,
    SourceLocation? Source = null);

internal sealed record TargetOptionIr(
    string Name,
    string Value,
    SourceLocation? Source = null);

internal sealed record NativeTargetFragmentIr(
    string Kind,
    string Payload,
    SourceLocation? Source = null);

internal static class CompilationTargetValidator
{
    public static IReadOnlyList<TargetDiagnostic> Validate(
        BehaviorIrDocument document,
        CompilationTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        return TargetCapabilityValidator.Validate(document, target)
            .Concat(TargetExtensionSemantics.Validate(document, target))
            .ToArray();
    }
}

internal static class TargetExtensionSemantics
{
    public const string NativeFragmentUnsupportedCode = "IKYD2042";
    public const string DuplicateTargetOptionCode = "IKYD2043";

    public static IReadOnlyList<TargetExtensionIr> Select(
        BehaviorIrDocument document,
        CompilationTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Traverse()
            .OfType<TargetExtensionIr>()
            .Where(extension => TargetSelectors.Matches(extension.Selector, target))
            .ToArray();
    }

    public static IReadOnlyList<TargetDiagnostic> Validate(
        BehaviorIrDocument document,
        CompilationTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);

        var backend = BackendCapabilities.Get(target);
        var extensions = Select(document, target);
        var diagnostics = new List<TargetDiagnostic>();
        var seenOptions = new Dictionary<string, TargetOptionIr>(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in extensions)
        {
            foreach (var requirement in extension.Requirements)
            {
                if (backend.Capabilities.Contains(requirement.Capability))
                    continue;

                diagnostics.Add(new TargetDiagnostic(
                    TargetCapabilityValidator.UnsupportedCapabilityCode,
                    $"`{CapabilityName(requirement.Capability)}` is not supported by target `{backend.Name}`.",
                    requirement.Source ?? extension.Source));
            }

            foreach (var option in extension.Options)
            {
                if (string.IsNullOrWhiteSpace(option.Name))
                    throw new InvalidDataException("Target option names must not be empty.");
                if (seenOptions.TryAdd(option.Name, option))
                    continue;

                diagnostics.Add(new TargetDiagnostic(
                    DuplicateTargetOptionCode,
                    $"target option `{option.Name}` is declared more than once for `{backend.Name}`.",
                    option.Source ?? extension.Source));
            }

            foreach (var fragment in extension.NativeFragments)
            {
                if (AllowsNativeFragments(target))
                    continue;

                diagnostics.Add(new TargetDiagnostic(
                    NativeFragmentUnsupportedCode,
                    $"native target fragments are not supported by target `{backend.Name}`.",
                    fragment.Source ?? extension.Source));
            }
        }

        return diagnostics;
    }

    public static bool AllowsNativeFragments(CompilationTarget target)
        => target is CompilationTarget.Qmk or CompilationTarget.Zmk;

    private static string CapabilityName(BehaviorCapability capability) => capability switch
    {
        BehaviorCapability.KeyOutput => "key-output",
        BehaviorCapability.Layer => "layer",
        BehaviorCapability.Combo => "combo",
        BehaviorCapability.HoldTap => "hold-tap",
        BehaviorCapability.ModTap => "mod-tap",
        BehaviorCapability.LayerTap => "layer-tap",
        BehaviorCapability.Macro => "macro",
        BehaviorCapability.Unicode => "unicode",
        BehaviorCapability.Pointer => "pointer",
        BehaviorCapability.HostCommand => "host-command",
        BehaviorCapability.Clipboard => "clipboard",
        BehaviorCapability.AppContext => "app-context",
        _ => capability.ToString(),
    };
}
