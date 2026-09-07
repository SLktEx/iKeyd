namespace iKeyd.App;

/// <summary>
/// Schedules at most one absolute monotonic Behavior deadline. The scheduler is a
/// backend wake-up mechanism only; Behavior state and timer semantics remain in
/// the target-neutral runtime.
/// </summary>
internal interface IBehaviorDeadlineScheduler : IDisposable
{
    void Schedule(long? deadlineMs, Action<long> callback);
}

internal sealed class ThreadPoolBehaviorDeadlineScheduler : IBehaviorDeadlineScheduler
{
    private readonly object _gate = new();
    private readonly Func<long> _clock;
    private readonly Timer _timer;
    private Action<long>? _callback;
    private long? _deadlineMs;
    private bool _disposed;

    public ThreadPoolBehaviorDeadlineScheduler(Func<long>? clock = null)
    {
        _clock = clock ?? static () => Environment.TickCount64;
        _timer = new Timer(
            OnTimer,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public void Schedule(long? deadlineMs, Action<long> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            if (_disposed)
                return;

            _deadlineMs = deadlineMs;
            _callback = deadlineMs is null ? null : callback;
            RearmLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _deadlineMs = null;
            _callback = null;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        _timer.Dispose();
    }

    private void OnTimer(object? state)
    {
        Action<long>? callback = null;
        long now = 0;

        lock (_gate)
        {
            if (_disposed || _deadlineMs is not long deadline)
                return;

            now = _clock();
            if (now < deadline)
            {
                // A callback from a superseded schedule may already have been
                // queued. Never let that stale wake-up fire a newer deadline early.
                RearmLocked();
                return;
            }

            callback = _callback;
            _deadlineMs = null;
            _callback = null;
        }

        callback?.Invoke(now);
    }

    private void RearmLocked()
    {
        if (_deadlineMs is not long deadline)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var dueMs = Math.Max(0, deadline - _clock());
        _timer.Change(TimeSpan.FromMilliseconds(dueMs), Timeout.InfiniteTimeSpan);
    }
}
