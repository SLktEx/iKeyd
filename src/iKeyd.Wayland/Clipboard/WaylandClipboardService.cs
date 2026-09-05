using iKeyd.Core.Clipboard;
using iKeyd.Core.Platform;

namespace iKeyd.Wayland.Clipboard;

public sealed class WaylandClipboardService : IClipboardService, IBackendCapabilityProvider
{
    private readonly IWaylandCommandRunner _runner;
    private readonly string _wlCopy;
    private readonly string _wlPaste;
    private readonly Timer? _pollTimer;
    private string? _lastText;
    private int _polling;
    private bool _disposed;

    public WaylandClipboardService(
        WaylandBackendOptions? options = null,
        IWaylandCommandRunner? runner = null,
        TimeSpan? pollInterval = null)
    {
        var resolved = options ?? WaylandBackendOptions.Detect();
        _runner = runner ?? new SystemWaylandCommandRunner();
        _wlCopy = resolved.WlCopyCommand;
        _wlPaste = resolved.WlPasteCommand;

        var isWayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
                        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var supported = new List<BackendCapability>();
        if (isWayland && _runner.Exists(_wlPaste))
        {
            supported.Add(BackendCapability.ClipboardRead);
            supported.Add(BackendCapability.ClipboardWatch);
        }
        if (isWayland && _runner.Exists(_wlCopy))
            supported.Add(BackendCapability.ClipboardWrite);
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
        Capabilities.Require(BackendCapability.ClipboardRead,
            "wl-paste and an active Wayland session are required.");
        return ReadTextCore();
    }

    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfDisposed();
        Capabilities.Require(BackendCapability.ClipboardWrite,
            "wl-copy and an active Wayland session are required.");

        var result = _runner.Run(
            _wlCopy,
            ["--type", "text/plain;charset=utf-8"],
            text,
            TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"wl-copy failed ({result.ExitCode}): {result.StandardError.Trim()}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pollTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? ReadTextCore()
    {
        var result = _runner.Run(
            _wlPaste,
            ["--no-newline", "--type", "text"],
            timeout: TimeSpan.FromSeconds(5));
        if (result.ExitCode == 0)
            return result.StandardOutput;

        var error = result.StandardError.Trim();
        if (error.Contains("nothing is copied", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("no selection", StringComparison.OrdinalIgnoreCase))
            return null;
        throw new InvalidOperationException($"wl-paste failed ({result.ExitCode}): {error}");
    }

    private string? TryReadSilently()
    {
        try { return ReadTextCore(); }
        catch { return null; }
    }

    private void PollClipboard(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0)
            return;

        try
        {
            var current = TryReadSilently();
            if (!string.Equals(current, _lastText, StringComparison.Ordinal))
            {
                _lastText = current;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
