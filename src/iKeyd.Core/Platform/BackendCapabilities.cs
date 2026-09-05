namespace iKeyd.Core.Platform;

public enum BackendCapability
{
    KeyboardInput,
    KeyboardOutput,
    TextOutputAscii,
    PointerRelative,
    PointerAbsolute,
    PointerButtons,
    PointerScroll,
    MediaKeys,
    ClipboardRead,
    ClipboardWrite,
    ClipboardWatch,
    WindowQuery,
    WindowMoveResize,
    WindowState,
    WindowActivation,
    WindowTopMost,
    WindowOpacity,
    WindowCaption
}

public interface IBackendCapabilityProvider
{
    BackendCapabilities Capabilities { get; }
}

public sealed class BackendCapabilities
{
    private readonly HashSet<BackendCapability> _supported;

    public BackendCapabilities(IEnumerable<BackendCapability> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);
        _supported = new HashSet<BackendCapability>(supported);
    }

    public static BackendCapabilities None { get; } = new([]);

    public IReadOnlySet<BackendCapability> Supported => _supported;

    public bool Supports(BackendCapability capability) => _supported.Contains(capability);

    public void Require(BackendCapability capability, string? detail = null)
    {
        if (!Supports(capability))
            throw new BackendCapabilityException(capability, detail);
    }
}

public sealed class BackendCapabilityException : NotSupportedException
{
    public BackendCapabilityException(BackendCapability capability, string? detail = null)
        : base(detail is null
            ? $"Backend capability '{capability}' is not available."
            : $"Backend capability '{capability}' is not available: {detail}")
        => Capability = capability;

    public BackendCapability Capability { get; }
}
