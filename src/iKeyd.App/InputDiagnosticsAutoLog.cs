using System.Text;

namespace iKeyd.App;

/// <summary>
/// Periodically persists the bounded in-memory input diagnostics snapshot without
/// putting file I/O on the keyboard hook hot path. The current file is atomically
/// replaced, so it stays bounded to the same recent-event window as the ring buffer.
/// </summary>
internal sealed class InputDiagnosticsAutoLog : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Func<string> _exportDiagnostics;
    private readonly string _logPath;
    private readonly string _previousPath;
    private readonly string _tempPath;
    private readonly Timer _timer;
    private bool _disposed;

    public InputDiagnosticsAutoLog(
        Func<string> exportDiagnostics,
        string? logPath = null,
        TimeSpan? flushInterval = null)
    {
        _exportDiagnostics = exportDiagnostics ?? throw new ArgumentNullException(nameof(exportDiagnostics));
        _logPath = logPath ?? DefaultLogPath;
        _previousPath = Path.Combine(
            Path.GetDirectoryName(_logPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(_logPath)}.previous{Path.GetExtension(_logPath)}");
        _tempPath = _logPath + ".tmp";

        TryPrepareLogDirectoryAndRotate();

        var interval = flushInterval ?? DefaultFlushInterval;
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(flushInterval));

        _timer = new Timer(
            static state => ((InputDiagnosticsAutoLog)state!).FlushBestEffort(),
            this,
            TimeSpan.Zero,
            interval);
    }

    internal static string DefaultLogDirectory
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iKeyd",
            "logs");

    internal static string DefaultLogPath
        => Path.Combine(DefaultLogDirectory, "input-diagnostics.log");

    internal string LogPath => _logPath;
    internal string PreviousLogPath => _previousPath;

    internal void FlushNow()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            WriteSnapshot();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            try
            {
                WriteSnapshot();
            }
            catch
            {
                // Diagnostics must never make iKeyd shutdown fail.
            }

            _disposed = true;
        }

        _timer.Dispose();
    }

    private void FlushBestEffort()
    {
        try
        {
            FlushNow();
        }
        catch
        {
            // A missing/unwritable LocalAppData path must not break input handling.
        }
    }

    private void TryPrepareLogDirectoryAndRotate()
    {
        try
        {
            var directory = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(_tempPath))
                File.Delete(_tempPath);

            if (File.Exists(_logPath))
                File.Move(_logPath, _previousPath, overwrite: true);
        }
        catch
        {
            // Best effort only. The periodic writer can recover later if the path
            // becomes writable again.
        }
    }

    private void WriteSnapshot()
    {
        var directory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var snapshot = _exportDiagnostics();
        File.WriteAllText(_tempPath, snapshot, Utf8NoBom);
        File.Move(_tempPath, _logPath, overwrite: true);
    }
}
