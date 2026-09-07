using iKeyd.Windows.Input;

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

/// <summary>
/// Default used by isolated router tests/callers that do not provide a platform
/// wake-up source. Event-driven AdvanceTo semantics continue to work unchanged.
/// </summary>
internal sealed class NoOpBehaviorDeadlineScheduler : IBehaviorDeadlineScheduler
{
    public void Schedule(long? deadlineMs, Action<long> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Production Windows adapter. The underlying keyboard hook owns the actual timer
/// so timeout callbacks are serialized through the same Windows message loop as
/// physical input instead of racing it on a ThreadPool thread.
/// </summary>
internal sealed class WindowsHookBehaviorDeadlineScheduler : IBehaviorDeadlineScheduler
{
    private readonly WindowsKeyboardBackend _keyboard;
    private bool _disposed;

    public WindowsHookBehaviorDeadlineScheduler(WindowsKeyboardBackend keyboard)
        => _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));

    public void Schedule(long? deadlineMs, Action<long> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (_disposed)
            return;

        _keyboard.ScheduleBehaviorDeadline(deadlineMs, callback);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _keyboard.ScheduleBehaviorDeadline(null, static _ => { });
    }
}
