using iKeyd.Core.Input;
using iKeyd.Core.Platform;
using iKeyd.Linux.Input;

namespace iKeyd.Wayland.Input;

public sealed class WaylandKeyboardBackend : IKeyboardInputSource, IKeyboardOutput, IBackendCapabilityProvider, IDisposable
{
    private readonly LinuxUInputDevice _uinput;
    private readonly LinuxEvdevKeyboardSource _input;
    private bool _disposed;

    public WaylandKeyboardBackend(WaylandBackendOptions? options = null, LinuxEvdevKeyMap? keyMap = null)
    {
        Options = options ?? WaylandBackendOptions.Detect();
        Probe = WaylandBackendProbe.Probe(Options);
        Probe.Capabilities.Require(BackendCapability.KeyboardInput,
            "Configure a readable /dev/input/by-id/*-event-kbd or /dev/input/by-path/*-event-kbd device, or IKEYD_INPUT_DEVICES.");
        Probe.Capabilities.Require(BackendCapability.KeyboardOutput,
            "A writable /dev/uinput is required for virtual keyboard output and grabbed-device pass-through.");
        if (Options.GrabPhysicalKeyboards)
            Probe.Capabilities.Require(BackendCapability.KeyboardSuppression,
                "Suppressing/remapping physical keys requires both EVIOCGRAB access and uinput pass-through.");

        var resolvedKeyMap = keyMap ?? new LinuxEvdevKeyMap();
        _uinput = new LinuxUInputDevice(Options.UInputPath, resolvedKeyMap);
        _input = new LinuxEvdevKeyboardSource(
            Options.KeyboardDevicePaths,
            _uinput,
            resolvedKeyMap,
            Options.GrabPhysicalKeyboards);
    }

    public WaylandBackendOptions Options { get; }
    public WaylandBackendProbeResult Probe { get; }
    public BackendCapabilities Capabilities => Probe.Capabilities;
    public bool IsRunning => _input.IsRunning;
    public Exception? LastInputError => _input.LastError;
    public LinuxUInputDevice UInputDevice => _uinput;

    public void Start(IKeyboardEventHandler handler)
    {
        ThrowIfDisposed();
        _input.Start(handler);
    }

    public void Stop() => _input.Stop();
    public void SendKey(KeyboardKey key, KeyEventKind kind) { ThrowIfDisposed(); _uinput.SendKey(key, kind); }
    public void SendKeyPress(KeyboardKey key) { ThrowIfDisposed(); _uinput.SendKeyPress(key); }
    public void SendText(string text) { ThrowIfDisposed(); _uinput.SendText(text); }
    public bool IsToggleOn(ushort virtualKey) => false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _input.Dispose(); }
        finally { _uinput.Dispose(); }
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
