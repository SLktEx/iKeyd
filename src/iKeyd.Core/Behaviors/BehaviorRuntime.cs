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

    public bool IsBound(KeyId key) => _bindings.ContainsKey(key);

    public BehaviorDispatchResult OnKeyDown(KeyId key, long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceActive(timestampMs, actions);

        foreach (var active in _active.Values)
        {
            if (active.SourceKey != key)
                active.OnInterrupt(key, timestampMs, actions);
        }

        if (!_bindings.TryGetValue(key, out var definition))
            return Result(false, actions);

        // Physical keyboard auto-repeat produces repeated downs without a matching
        // up. A behavior press is a single state-machine instance, so repeated downs
        // are suppressed but do not restart timers or duplicate local state.
        if (!_active.ContainsKey(key))
            _active.Add(key, definition.CreateInstance(key, timestampMs));

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
