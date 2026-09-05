using iKeyd.Core.Clipboard;
using iKeyd.Core.Platform;

namespace iKeyd.X11.Clipboard;

public sealed class X11ClipboardService : IClipboardService, IBackendCapabilityProvider
{
    private readonly IX11CommandRunner _runner;
    private readonly string _xclip;
    private readonly Timer? _pollTimer;
    private string? _lastText;
    private int _polling;
    private bool _disposed;

    public X11ClipboardService(
        X11BackendOptions? options = null,
        IX11CommandRunner? runner = null,
        TimeSpan? pollInterval = null,
        bool? hasDisplay = null)
    {
        var resolved = options ?? X11BackendOptions.Detect();
        _runner = runner ?? new SystemX11CommandRunner();
        _xclip = resolved.XclipCommand;
        var displayAvailable = hasDisplay ?? !string.IsNullOrWhiteSpace(resolved.DisplayName ?? Environment.GetEnvironmentVariable("DISPLAY"));
        var supported = new List<BackendCapability>();
        if (displayAvailable && _runner.Exists(_xclip))
        {
            supported.Add(BackendCapability.ClipboardRead);
            supported.Add(BackendCapability.ClipboardWrite);
            supported.Add(BackendCapability.ClipboardWatch);
        }
        Capabilities = new BackendCapabilities(supported);

        if (Capabilities.Supports(BackendCapability.ClipboardRead))
        {
            _lastText = TryReadSilently();
            var interval = pollInterval ?? TimeSpan.FromMilliseconds(750);
            _pollTimer = new Timer(PollClipboard, null, interval, interval);
        }
    }

    public event EventHandler? Changed;
    public BackendCapabilities Capabilities { get; }

    public string? ReadText()
    {
        ThrowIfDisposed();
        Capabilities.Require(BackendCapability.ClipboardRead, "An X11 DISPLAY and xclip are required.");
        return ReadTextCore();
    }

    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfDisposed();
        Capabilities.Require(BackendCapability.ClipboardWrite, "An X11 DISPLAY and xclip are required.");
        var result = _runner.Run(_xclip, ["-selection", "clipboard", "-in"], text, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"xclip write failed with exit code {result.ExitCode}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? ReadTextCore()
    {
        var result = _runner.Run(_xclip, ["-selection", "clipboard", "-out"], timeout: TimeSpan.FromSeconds(5));
        if (result.ExitCode == 0) return result.StandardOutput;
        return null;
    }

    private string? TryReadSilently()
    {
        try { return ReadTextCore(); }
        catch { return null; }
    }

    private void PollClipboard(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            var current = TryReadSilently();
            if (!string.Equals(current, _lastText, StringComparison.Ordinal))
            {
                _lastText = current;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
