using iKeyd.Core.Chords;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// Definition shared by every key using the same behavior implementation.
/// A fresh runtime instance is created for each physical press.
/// </summary>
public abstract class BehaviorDefinition
{
    internal abstract BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs);
}

/// <summary>
/// Per-press state machine used by <see cref="BehaviorRuntime"/>.
/// Implementations emit only primitive <see cref="BehaviorAction"/> values.
/// </summary>
public abstract class BehaviorInstance
{
    protected BehaviorInstance(KeyId sourceKey)
    {
        SourceKey = sourceKey;
    }

    public KeyId SourceKey { get; }

    /// <summary>
    /// Earliest absolute monotonic timestamp at which this instance needs an
    /// explicit time advance. Null means the instance has no pending deadline.
    /// </summary>
    internal virtual long? NextDeadlineMs => null;

    internal virtual void OnPress(long timestampMs, List<BehaviorAction> actions)
    {
    }

    /// <summary>
    /// Receives a repeated physical key-down for the same still-active source key.
    /// Stateful behaviors ignore this by default so auto-repeat cannot replay layer
    /// or modifier transitions. Repeatable output behaviors may override it.
    /// </summary>
    internal virtual void OnRepeat(long timestampMs, List<BehaviorAction> actions)
    {
    }

    internal virtual void AdvanceTo(long timestampMs, List<BehaviorAction> actions)
    {
    }

    internal virtual void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
    {
    }

    internal abstract void OnRelease(long timestampMs, List<BehaviorAction> actions);

    internal virtual void Cancel(List<BehaviorAction> actions)
    {
    }
}

/// <summary>
/// Platform-neutral event runtime for key behaviors. It receives both key-down and
/// key-up events, observes unrelated key-downs as interruptions, and advances
/// behavior timers using caller-supplied timestamps. It deliberately knows nothing
/// about Windows, Wayland, QMK helpers, or a specific DSL surface syntax.
/// </summary>
public sealed class BehaviorRuntime
{
    private readonly Dictionary<KeyId, BehaviorDefinition> _bindings;
    private readonly Dictionary<KeyId, BehaviorInstance> _active = [];
    private bool _hasTimestamp;
    private long _lastTimestampMs;

    public BehaviorRuntime(IEnumerable<KeyValuePair<KeyId, BehaviorDefinition>> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = new Dictionary<KeyId, BehaviorDefinition>();
        foreach (var pair in bindings)
        {
            ArgumentNullException.ThrowIfNull(pair.Value);
            _bindings[pair.Key] = pair.Value;
        }
    }

    public int ActiveCount => _active.Count;

    /// <summary>
    /// Earliest pending deadline across all active behavior instances. Backends
    /// may schedule one bounded wake-up for this value rather than polling or
    /// introducing a scripting scheduler.
    /// </summary>
    public long? NextDeadlineMs
    {
        get
        {
            long? next = null;
            foreach (var instance in _active.Values)
            {
                if (instance.NextDeadlineMs is not long deadline)
                    continue;
                if (next is null || deadline < next.Value)
                    next = deadline;
            }
            return next;
        }
    }

    public bool IsBound(KeyId key) => _bindings.ContainsKey(key);
    public bool IsActive(KeyId key) => _active.ContainsKey(key);

    public BehaviorDispatchResult OnKeyDown(KeyId key, long timestampMs)
    {
        var observed = ObserveKeyDown(key, timestampMs);
        var started = BeginKeyDown(key, timestampMs);
        if (observed.Actions.Count == 0)
            return started;
        if (started.Actions.Count == 0)
            return new BehaviorDispatchResult(started.Suppress, observed.Actions);

        var actions = new List<BehaviorAction>(observed.Actions.Count + started.Actions.Count);
        actions.AddRange(observed.Actions);
        actions.AddRange(started.Actions);
        return new BehaviorDispatchResult(started.Suppress, actions);
    }

    /// <summary>
    /// Advances active instances and reports an unrelated key-down as an
    /// interruption without starting a newly bound behavior. Routers can use this
    /// before layer resolution, apply emitted actions, then decide which keymap's
    /// binding should start for the same physical key event.
    /// </summary>
    public BehaviorDispatchResult ObserveKeyDown(KeyId key, long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceActive(timestampMs, actions);

        foreach (var active in _active.Values)
        {
            if (active.SourceKey != key)
                active.OnInterrupt(key, timestampMs, actions);
        }

        return Result(false, actions);
    }

    /// <summary>
    /// Starts the behavior bound to <paramref name="key"/> without delivering a
    /// second interruption notification to already-active instances. A repeated
    /// physical down is delivered to the existing instance instead of creating a
    /// second instance or restarting its state.
    /// </summary>
    public BehaviorDispatchResult BeginKeyDown(KeyId key, long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceActive(timestampMs, actions);

        if (!_bindings.TryGetValue(key, out var definition))
            return Result(false, actions);

        if (_active.TryGetValue(key, out var active))
        {
            active.OnRepeat(timestampMs, actions);
            return Result(true, actions);
        }

        var instance = definition.CreateInstance(key, timestampMs);
        instance.OnPress(timestampMs, actions);
        _active.Add(key, instance);
        return Result(true, actions);
    }

    public BehaviorDispatchResult OnKeyUp(KeyId key, long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceActive(timestampMs, actions);

        if (!_active.Remove(key, out var instance))
            return Result(false, actions);

        instance.OnRelease(timestampMs, actions);
        return Result(true, actions);
    }

    /// <summary>
    /// Advances timers without introducing an input event.
    /// </summary>
    public IReadOnlyList<BehaviorAction> AdvanceTo(long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceActive(timestampMs, actions);
        return actions.Count == 0 ? Array.Empty<BehaviorAction>() : actions;
    }

    /// <summary>
    /// Cancels every active behavior and emits cleanup actions for resources owned
    /// by those instances. Cancellation does not emit pending tap actions.
    /// </summary>
    public IReadOnlyList<BehaviorAction> CancelAll()
    {
        if (_active.Count == 0)
            return Array.Empty<BehaviorAction>();

        var actions = new List<BehaviorAction>();
        foreach (var instance in _active.Values)
            instance.Cancel(actions);
        _active.Clear();
        return actions.Count == 0 ? Array.Empty<BehaviorAction>() : actions;
    }

    private void AdvanceActive(long timestampMs, List<BehaviorAction> actions)
    {
        foreach (var instance in _active.Values)
            instance.AdvanceTo(timestampMs, actions);
    }

    private void EnsureMonotonic(long timestampMs)
    {
        if (_hasTimestamp && timestampMs < _lastTimestampMs)
            throw new ArgumentOutOfRangeException(nameof(timestampMs), "Timestamps must be monotonic.");

        _hasTimestamp = true;
        _lastTimestampMs = timestampMs;
    }

    private static BehaviorDispatchResult Result(bool suppress, List<BehaviorAction> actions)
        => new(
            suppress,
            actions.Count == 0 ? Array.Empty<BehaviorAction>() : actions);
}
