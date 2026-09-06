using iKeyd.Core.Automation;

namespace iKeyd.Windows.Automation;

public sealed class WindowsSystemQueryCache : ISystemQuerySnapshot, IDisposable
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly ISystemQueryProvider _provider;
    private readonly string[] _keys;
    private readonly SystemQuerySnapshotStore _snapshot = new();
    private readonly System.Threading.Timer? _timer;
    private int _refreshing;
    private bool _disposed;

    public WindowsSystemQueryCache(
        ISystemQueryProvider provider,
        IEnumerable<string> keys,
        TimeSpan? refreshInterval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys
            .Select(SystemQueryKeys.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Refresh();
        if (_keys.Length != 0)
        {
            var interval = refreshInterval ?? DefaultRefreshInterval;
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(refreshInterval), "Refresh interval must be positive.");
            _timer = new System.Threading.Timer(_ => Refresh(), null, interval, interval);
        }
    }

    public bool TryGetValue(string key, out string value)
        => _snapshot.TryGetValue(key, out value);

    internal void Refresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0)
            return;

        try
        {
            var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _keys)
            {
                try
                {
                    next[key] = _provider.GetValue(key);
                }
                catch
                {
                    if (_snapshot.TryGetValue(key, out var previous))
                        next[key] = previous;
                }
            }
            _snapshot.Publish(next);
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer?.Dispose();
    }
}
