using iKeyd.Core.Input;
using iKeyd.Core.Platform;
using iKeyd.Linux.Input;

namespace iKeyd.X11.Input;

public sealed class X11KeyboardBackend : IKeyboardInputSource, IKeyboardOutput, IBackendCapabilityProvider, IDisposable
{
    private readonly LinuxUInputDevice _uinput;
    private readonly LinuxEvdevKeyboardSource _input;
    private bool _disposed;

    public X11KeyboardBackend(X11BackendOptions? options = null, LinuxEvdevKeyMap? keyMap = null)
    {
        Options = options ?? X11BackendOptions.Detect();
        Probe = X11BackendProbe.Probe(Options);
        Probe.Capabilities.Require(BackendCapability.KeyboardInput, "A readable evdev keyboard is required.");
        Probe.Capabilities.Require(BackendCapability.KeyboardOutput, "A writable uinput device is required.");
        if (Options.GrabPhysicalKeyboards)
            Probe.Capabilities.Require(BackendCapability.KeyboardSuppression, "EVIOCGRAB plus uinput pass-through is required.");

        var resolved = keyMap ?? new LinuxEvdevKeyMap();
        _uinput = new LinuxUInputDevice(Options.UInputPath, resolved);
        _input = new LinuxEvdevKeyboardSource(Options.KeyboardDevicePaths, _uinput, resolved, Options.GrabPhysicalKeyboards);
    }

    public X11BackendOptions Options { get; }
    public X11BackendProbeResult Probe { get; }
    public BackendCapabilities Capabilities => Probe.Capabilities;
    public bool IsRunning => _input.IsRunning;
    public Exception? LastInputError => _input.LastError;
    public LinuxUInputDevice UInputDevice => _uinput;

    public void Start(IKeyboardEventHandler handler) { ThrowIfDisposed(); _input.Start(handler); }
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
