using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

namespace iKeyd.Core.Runtime;

public enum KeyBehaviorTransitionKind
{
    Tap,
    HoldStarted,
    HoldEnded
}

public readonly record struct KeyBehaviorTransition(KeyBehaviorTransitionKind Kind, KeyBehaviorAction Action);

/// <summary>
/// Zero-to-two transitions produced by one physical keyboard event. Two are
/// sufficient for timeout-on-release (HoldStarted + HoldEnded) and for resolving
/// one pending behavior while starting an immediate hold on the current key.
/// </summary>
public readonly struct KeyBehaviorTransitionList
{
    private readonly KeyBehaviorTransition _first;
    private readonly KeyBehaviorTransition _second;

    public KeyBehaviorTransitionList(KeyBehaviorTransition first)
    {
        _first = first;
        _second = default;
        Count = 1;
    }

    public KeyBehaviorTransitionList(KeyBehaviorTransition first, KeyBehaviorTransition second)
    {
        _first = first;
        _second = second;
        Count = 2;
    }

    public int Count { get; }

    public KeyBehaviorTransition this[int index] => index switch
    {
        0 when Count >= 1 => _first,
        1 when Count >= 2 => _second,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public readonly record struct KeyBehaviorEventResult(bool Consumed, KeyBehaviorTransitionList Transitions);

public readonly record struct ActiveKeyBehaviorHold(KeyId Trigger, KeyBehaviorAction Action);

/// <summary>
/// Generic tap/hold resolver used only for DSL-configured behaviors. It keeps at
/// most one undecided tap/hold key, while already-resolved holds can overlap.
/// No timer is required: timeout is advanced by the timestamp of the next input
/// event, which is sufficient because a hold has no observable keyboard effect
/// until another event occurs.
/// </summary>
public sealed class ConfiguredKeyBehaviorRuntime
{
    private readonly KeyBehaviorProfile _profile;
    private readonly List<ActiveKeyBehaviorHold> _active = new(4);
    private PendingBehavior? _pending;

    public ConfiguredKeyBehaviorRuntime(KeyBehaviorProfile profile)
        => _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public int ActiveHoldCount => _active.Count;

    public ActiveKeyBehaviorHold GetActiveHoldAt(int index) => _active[index];

    public KeyBehaviorEventResult OnKeyDown(KeyId key, long timestampMs)
    {
        var transitions = new TransitionBuilder();
        AdvancePending(timestampMs, ref transitions);

        if (_pending is { } pending)
        {
            if (pending.Binding.Trigger == key)
                return new KeyBehaviorEventResult(true, transitions.Build());

            ResolveInterruptedPending(ref transitions);
        }

        if (!_profile.TryGetBehavior(key, out var binding))
            return new KeyBehaviorEventResult(false, transitions.Build());

        if (binding.Tap is null)
        {
            StartHold(binding, ref transitions);
        }
        else
        {
            _pending = new PendingBehavior(binding, timestampMs);
        }

        return new KeyBehaviorEventResult(true, transitions.Build());
    }

    public KeyBehaviorEventResult OnKeyUp(KeyId key, long timestampMs)
    {
        var transitions = new TransitionBuilder();

        if (_pending is { } pending && pending.Binding.Trigger == key)
        {
            _pending = null;
            if (HasTimedOut(pending, timestampMs))
            {
                StartHold(pending.Binding, ref transitions);
                EndHold(key, ref transitions);
            }
            else
            {
                transitions.Add(new KeyBehaviorTransition(
                    KeyBehaviorTransitionKind.Tap,
                    pending.Binding.Tap ?? throw new InvalidOperationException("Pending behavior has no tap action.")));
            }

            return new KeyBehaviorEventResult(true, transitions.Build());
        }

        AdvancePending(timestampMs, ref transitions);
        if (EndHold(key, ref transitions))
            return new KeyBehaviorEventResult(true, transitions.Build());

        return new KeyBehaviorEventResult(false, transitions.Build());
    }

    public void Reset()
    {
        _pending = null;
        _active.Clear();
    }

    private void AdvancePending(long timestampMs, ref TransitionBuilder transitions)
    {
        if (_pending is not { } pending || !HasTimedOut(pending, timestampMs))
            return;

        _pending = null;
        StartHold(pending.Binding, ref transitions);
    }

    private void ResolveInterruptedPending(ref TransitionBuilder transitions)
    {
        var pending = _pending ?? throw new InvalidOperationException("No pending behavior to resolve.");
        _pending = null;

        if (pending.Binding.Interrupt == TapHoldInterruptPolicy.Hold)
        {
            StartHold(pending.Binding, ref transitions);
            return;
        }

        transitions.Add(new KeyBehaviorTransition(
            KeyBehaviorTransitionKind.Tap,
            pending.Binding.Tap ?? throw new InvalidOperationException("Pending behavior has no tap action.")));
    }

    private void StartHold(KeyBehaviorBinding binding, ref TransitionBuilder transitions)
    {
        if (_active.Any(item => item.Trigger == binding.Trigger))
            return;

        _active.Add(new ActiveKeyBehaviorHold(binding.Trigger, binding.Hold));
        transitions.Add(new KeyBehaviorTransition(KeyBehaviorTransitionKind.HoldStarted, binding.Hold));
    }

    private bool EndHold(KeyId trigger, ref TransitionBuilder transitions)
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            if (_active[index].Trigger != trigger)
                continue;

            var hold = _active[index];
            _active.RemoveAt(index);
            transitions.Add(new KeyBehaviorTransition(KeyBehaviorTransitionKind.HoldEnded, hold.Action));
            return true;
        }

        return false;
    }

    private static bool HasTimedOut(PendingBehavior pending, long timestampMs)
        => timestampMs - pending.PressedAtMs >= pending.Binding.TimeoutMs;

    private readonly record struct PendingBehavior(KeyBehaviorBinding Binding, long PressedAtMs);

    private struct TransitionBuilder
    {
        private KeyBehaviorTransition _first;
        private KeyBehaviorTransition _second;
        private byte _count;

        public void Add(KeyBehaviorTransition transition)
        {
            switch (_count)
            {
                case 0:
                    _first = transition;
                    _count = 1;
                    break;
                case 1:
                    _second = transition;
                    _count = 2;
                    break;
                default:
                    throw new InvalidOperationException("A behavior event produced more than two transitions.");
            }
        }

        public readonly KeyBehaviorTransitionList Build() => _count switch
        {
            0 => default,
            1 => new KeyBehaviorTransitionList(_first),
            2 => new KeyBehaviorTransitionList(_first, _second),
            _ => throw new InvalidOperationException()
        };
    }
}
