internal enum BehaviorCapability
{
    KeyOutput,
    Layer,
    Combo,
    HoldTap,
    ModTap,
    LayerTap,
    Macro,
    Unicode,
    Pointer,
    HostCommand,
    Clipboard,
    AppContext,
}

internal enum CompilationTarget
{
    IKeydCSharp,
    IKeydRust,
    Qmk,
    Zmk,
    JsonDebug,
}

internal readonly record struct SourceLocation(string Path, int Line, int Column)
{
    public override string ToString() => $"{Path}:{Line}:{Column}";
}

internal readonly record struct KeyPosition
{
    public KeyPosition(int row, int column)
    {
        if (row < 0)
            throw new ArgumentOutOfRangeException(nameof(row), "Keyboard row must be non-negative.");
        if (column < 0)
            throw new ArgumentOutOfRangeException(nameof(column), "Keyboard column must be non-negative.");

        Row = row;
        Column = column;
    }

    public int Row { get; }

    public int Column { get; }

    public override string ToString() => $"POS[{Row},{Column}]";
}

internal sealed record BehaviorIrDocument(IReadOnlyList<BehaviorIrNode> Nodes)
{
    public IEnumerable<BehaviorIrNode> Traverse()
    {
        foreach (var node in Nodes)
        {
            foreach (var descendant in Traverse(node))
                yield return descendant;
        }
    }

    private static IEnumerable<BehaviorIrNode> Traverse(BehaviorIrNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Traverse(child))
                yield return descendant;
        }
    }
}

internal abstract record BehaviorIrNode(SourceLocation? Source)
{
    public abstract IEnumerable<BehaviorCapability> RequiredCapabilities { get; }

    public virtual IEnumerable<BehaviorIrNode> Children => Array.Empty<BehaviorIrNode>();
}

internal sealed record KeyBindingIr(
    KeyPosition Position,
    BehaviorIrNode Behavior,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities => Array.Empty<BehaviorCapability>();

    public override IEnumerable<BehaviorIrNode> Children
    {
        get
        {
            yield return Behavior;
        }
    }
}

internal sealed record KeyOutputIr(
    string Key,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.KeyOutput;
        }
    }
}

internal sealed record LayerMomentaryIr(
    string Layer,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.Layer;
        }
    }
}

internal sealed record LayerTapIr(
    string Layer,
    BehaviorIrNode TapBehavior,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.Layer;
            yield return BehaviorCapability.LayerTap;
        }
    }

    public override IEnumerable<BehaviorIrNode> Children
    {
        get
        {
            yield return TapBehavior;
        }
    }
}

internal sealed record ModTapIr(
    string Modifier,
    BehaviorIrNode TapBehavior,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.ModTap;
        }
    }

    public override IEnumerable<BehaviorIrNode> Children
    {
        get
        {
            yield return TapBehavior;
        }
    }
}

internal sealed record HoldTapIr(
    BehaviorIrNode HoldBehavior,
    BehaviorIrNode TapBehavior,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.HoldTap;
        }
    }

    public override IEnumerable<BehaviorIrNode> Children
    {
        get
        {
            yield return HoldBehavior;
            yield return TapBehavior;
        }
    }
}

internal sealed record ComboIr(
    IReadOnlyList<KeyPosition> Positions,
    BehaviorIrNode Behavior,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.Combo;
        }
    }

    public override IEnumerable<BehaviorIrNode> Children
    {
        get
        {
            yield return Behavior;
        }
    }
}

internal sealed record MacroIr(
    IReadOnlyList<BehaviorIrNode> Steps,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.Macro;
        }
    }

    public override IEnumerable<BehaviorIrNode> Children => Steps;
}

internal sealed record UnicodeIr(
    string Text,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.Unicode;
        }
    }
}

internal sealed record PointerIr(
    string Operation,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.Pointer;
        }
    }
}

internal sealed record HostCommandIr(
    string Command,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.HostCommand;
        }
    }
}

internal sealed record ClipboardIr(
    string Operation,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.Clipboard;
        }
    }
}

internal sealed record AppContextIr(
    string Matcher,
    BehaviorIrNode Behavior,
    SourceLocation? Source = null) : BehaviorIrNode(Source)
{
    public override IEnumerable<BehaviorCapability> RequiredCapabilities
    {
        get
        {
            yield return BehaviorCapability.AppContext;
        }
    }

    public override IEnumerable<BehaviorIrNode> Children
    {
        get
        {
            yield return Behavior;
        }
    }
}

internal sealed record BackendDescriptor(
    CompilationTarget Target,
    string Name,
    bool CodegenAvailable,
    IReadOnlySet<BehaviorCapability> Capabilities);

internal static class BackendCapabilities
{
    private static readonly IReadOnlySet<BehaviorCapability> HostCapabilities = new HashSet<BehaviorCapability>(
        Enum.GetValues<BehaviorCapability>());

    private static readonly IReadOnlySet<BehaviorCapability> QmkCapabilities = new HashSet<BehaviorCapability>
    {
        BehaviorCapability.KeyOutput,
        BehaviorCapability.Layer,
        BehaviorCapability.Combo,
        BehaviorCapability.HoldTap,
        BehaviorCapability.ModTap,
        BehaviorCapability.LayerTap,
        BehaviorCapability.Macro,
        BehaviorCapability.Pointer,
    };

    private static readonly IReadOnlySet<BehaviorCapability> ZmkCapabilities = new HashSet<BehaviorCapability>
    {
        BehaviorCapability.KeyOutput,
        BehaviorCapability.Layer,
        BehaviorCapability.Combo,
        BehaviorCapability.HoldTap,
        BehaviorCapability.ModTap,
        BehaviorCapability.LayerTap,
        BehaviorCapability.Macro,
    };

    public static BackendDescriptor Get(CompilationTarget target) => target switch
    {
        CompilationTarget.IKeydCSharp => new(target, "ikeyd-csharp", true, HostCapabilities),
        CompilationTarget.IKeydRust => new(target, "ikeyd-rust", false, HostCapabilities),
        CompilationTarget.Qmk => new(target, "qmk", false, QmkCapabilities),
        CompilationTarget.Zmk => new(target, "zmk", false, ZmkCapabilities),
        CompilationTarget.JsonDebug => new(target, "json", true, HostCapabilities),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown compilation target."),
    };
}

internal sealed record TargetDiagnostic(
    string Code,
    string Message,
    SourceLocation? Source)
{
    public override string ToString()
    {
        var prefix = Source is null ? string.Empty : $"{Source}: ";
        return $"{prefix}error {Code}: {Message}";
    }
}

internal static class TargetCapabilityValidator
{
    public const string UnsupportedCapabilityCode = "IKYD2041";

    public static IReadOnlyList<TargetDiagnostic> Validate(
        BehaviorIrDocument document,
        CompilationTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);

        var backend = BackendCapabilities.Get(target);
        var diagnostics = new List<TargetDiagnostic>();

        foreach (var node in document.Traverse())
        {
            foreach (var capability in node.RequiredCapabilities.Distinct())
            {
                if (backend.Capabilities.Contains(capability))
                    continue;

                diagnostics.Add(new TargetDiagnostic(
                    UnsupportedCapabilityCode,
                    $"`{CapabilityName(capability)}` is not supported by target `{backend.Name}`.",
                    node.Source));
            }
        }

        return diagnostics;
    }

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
