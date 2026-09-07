using iKeyd.Core.Chords;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// Definition shared by every key using the same behavior implementation.
/// A fresh runtime instance is created when no bounded pending instance exists for
/// the same source key.
/// </summary>
public abstract class BehaviorDefinition
{
    internal abstract BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs);
}

/// <summary>
/// Per-sequence state machine used by <see cref="BehaviorRuntime"/>.
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

    /// <summary>
    /// Whether this instance must remain alive after its source key is released.
    /// Retained instances are required to expose a finite <see cref="NextDeadlineMs"/>
    /// so post-release state cannot become an unbounded background task.
    /// </summary>
    internal virtual bool KeepAliveAfterRelease => false;

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
/// key-up events, observes unrelated key-downs as interruptions, advances bounded
/// deadlines, and may retain explicitly bounded post-release state. It deliberately
/// knows nothing about Windows, Wayland, QMK helpers, or a specific DSL surface.
/// </summary>
public sealed class BehaviorRuntime
{
    private readonly Dictionary<KeyId, BehaviorDefinition> _bindings;
    private readonly Dictionary<KeyId, BehaviorInstance> _active = [];
    private readonly Dictionary<KeyId, BehaviorInstance> _pending = [];
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
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Earliest pending deadline across physically-active and bounded retained
    /// behavior instances. Backends may schedule one wake-up for this value rather
    /// than polling or introducing a general scripting scheduler.
    /// </summary>
    public long? NextDeadlineMs
    {
        get
        {
            long? next = null;
            FindEarlierDeadline(_active.Values, ref next);
            FindEarlierDeadline(_pending.Values, ref next);
            return next;
        }
    }

    public bool IsBound(KeyId key) => _bindings.ContainsKey(key);
    public bool IsActive(KeyId key) => _active.ContainsKey(key);
    public bool IsPending(KeyId key) => _pending.ContainsKey(key);

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
    /// Advances active/pending instances and reports an unrelated key-down as an
    /// interruption without starting a newly bound behavior. A pending instance for
    /// the same source key is not interrupted; BeginKeyDown can resume it.
    /// </summary>
    public BehaviorDispatchResult ObserveKeyDown(KeyId key, long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceInstances(timestampMs, actions);

        foreach (var active in _active.Values)
        {
            if (active.SourceKey != key)
                active.OnInterrupt(key, timestampMs, actions);
        }

        foreach (var pending in _pending.Values)
        {
            if (pending.SourceKey != key)
                pending.OnInterrupt(key, timestampMs, actions);
        }

        PrunePending();
        return Result(false, actions);
    }

    /// <summary>
    /// Starts the behavior bound to <paramref name="key"/> without delivering a
    /// second interruption notification to already-active instances. A repeated
    /// physical down is delivered to the existing active instance. A bounded
    /// post-release instance for the same key is resumed before creating a new one.
    /// </summary>
    public BehaviorDispatchResult BeginKeyDown(KeyId key, long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceInstances(timestampMs, actions);

        if (_active.TryGetValue(key, out var active))
        {
            active.OnRepeat(timestampMs, actions);
            return Result(true, actions);
        }

        if (_pending.Remove(key, out var pending))
        {
            pending.OnPress(timestampMs, actions);
            _active.Add(key, pending);
            return Result(true, actions);
        }

        if (!_bindings.TryGetValue(key, out var definition))
            return Result(false, actions);

        var instance = definition.CreateInstance(key, timestampMs);
        instance.OnPress(timestampMs, actions);
        _active.Add(key, instance);
        return Result(true, actions);
    }

    public BehaviorDispatchResult OnKeyUp(KeyId key, long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceInstances(timestampMs, actions);

        if (!_active.Remove(key, out var instance))
            return Result(false, actions);

        instance.OnRelease(timestampMs, actions);
        if (instance.KeepAliveAfterRelease)
        {
            EnsureBoundedPending(instance);
            _pending.Add(key, instance);
        }

        return Result(true, actions);
    }

    /// <summary>
    /// Advances timers without introducing an input event.
    /// </summary>
    public IReadOnlyList<BehaviorAction> AdvanceTo(long timestampMs)
    {
        EnsureMonotonic(timestampMs);
        var actions = new List<BehaviorAction>();
        AdvanceInstances(timestampMs, actions);
        return actions.Count == 0 ? Array.Empty<BehaviorAction>() : actions;
    }

    /// <summary>
    /// Cancels every active or bounded-pending behavior and emits cleanup actions
    /// for resources owned by those instances. Cancellation does not resolve a
    /// pending tap/multi-tap sequence into normal output.
    /// </summary>
    public IReadOnlyList<BehaviorAction> CancelAll()
    {
        if (_active.Count == 0 && _pending.Count == 0)
            return Array.Empty<BehaviorAction>();

        var actions = new List<BehaviorAction>();
        foreach (var instance in _active.Values)
            instance.Cancel(actions);
        foreach (var instance in _pending.Values)
            instance.Cancel(actions);
        _active.Clear();
        _pending.Clear();
        return actions.Count == 0 ? Array.Empty<BehaviorAction>() : actions;
    }

    private void AdvanceInstances(long timestampMs, List<BehaviorAction> actions)
    {
        foreach (var instance in _active.Values)
            instance.AdvanceTo(timestampMs, actions);
        foreach (var instance in _pending.Values)
            instance.AdvanceTo(timestampMs, actions);
        PrunePending();
    }

    private void PrunePending()
    {
        List<KeyId>? completed = null;
        foreach (var pair in _pending)
        {
            if (pair.Value.KeepAliveAfterRelease)
            {
                EnsureBoundedPending(pair.Value);
                continue;
            }

            completed ??= [];
            completed.Add(pair.Key);
        }

        if (completed is null)
            return;
        foreach (var key in completed)
            _pending.Remove(key);
    }

    private static void EnsureBoundedPending(BehaviorInstance instance)
    {
        if (instance.NextDeadlineMs is null)
        {
            throw new InvalidOperationException(
                $"Behavior on '{instance.SourceKey}' requested post-release retention without a bounded deadline.");
        }
    }

    private static void FindEarlierDeadline(
        IEnumerable<BehaviorInstance> instances,
        ref long? next)
    {
        foreach (var instance in instances)
        {
            if (instance.NextDeadlineMs is not long deadline)
                continue;
            if (next is null || deadline < next.Value)
                next = deadline;
        }
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
