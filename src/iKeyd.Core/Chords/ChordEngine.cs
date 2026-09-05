using iKeyd.Core.Keymaps;

namespace iKeyd.Core.Chords;

public enum ChordEngineState
{
    Idle,
    PendingSingle
}

public sealed class ChordEngine<TOutput> where TOutput : notnull
{
    public const int DefaultChordWindowMs = 40;

    private readonly Keymap<TOutput> _keymap;
    private readonly long _chordWindowMs;
    private PendingKey? _pending;

    public ChordEngine(Keymap<TOutput> keymap, int chordWindowMs = DefaultChordWindowMs)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        if (chordWindowMs < 0)
            throw new ArgumentOutOfRangeException(nameof(chordWindowMs));

        _keymap = keymap;
        _chordWindowMs = chordWindowMs;
    }

    public ChordEngineState State => _pending is null
        ? ChordEngineState.Idle
        : ChordEngineState.PendingSingle;

    public KeyId? PendingKeyId => _pending?.Key;

    /// <summary>
    /// Processes a key-down without allocating a result collection. The engine can
    /// produce at most one output for a single input event, so the result is exposed
    /// as the conventional Try-pattern instead of an IReadOnlyList.
    /// </summary>
    public bool TryOnKeyDown(KeyId key, long timestampMs, out TOutput output)
    {
        if (_pending is null)
        {
            _pending = new PendingKey(key, timestampMs);
            output = default!;
            return false;
        }

        EnsureMonotonic(timestampMs);

        var pending = _pending.Value;
        var elapsed = timestampMs - pending.TimestampMs;

        if (elapsed <= _chordWindowMs && _keymap.TryGetChord(pending.Key, key, out var chordOutput))
        {
            _pending = null;
            output = chordOutput;
            return true;
        }

        var hasOutput = _keymap.TryGetSingle(pending.Key, out var singleOutput);
        _pending = new PendingKey(key, timestampMs);
        output = hasOutput ? singleOutput : default!;
        return hasOutput;
    }

    /// <summary>
    /// Advances the chord timeout without allocating a result collection.
    /// </summary>
    public bool TryAdvanceTo(long timestampMs, out TOutput output)
    {
        if (_pending is null)
        {
            output = default!;
            return false;
        }

        EnsureMonotonic(timestampMs);

        // The legacy chord condition is inclusive (<= 40 ms). Keeping the
        // pending key at exactly the boundary allows a key-down at 40 ms to be
        // processed before timeout expiry in a deterministic event loop.
        if (timestampMs - _pending.Value.TimestampMs <= _chordWindowMs)
        {
            output = default!;
            return false;
        }

        var pending = _pending.Value;
        _pending = null;
        return _keymap.TryGetSingle(pending.Key, out output!);
    }

    /// <summary>
    /// Flushes a pending single without allocating a result collection.
    /// </summary>
    public bool TryFlush(out TOutput output)
    {
        if (_pending is null)
        {
            output = default!;
            return false;
        }

        var pending = _pending.Value;
        _pending = null;
        return _keymap.TryGetSingle(pending.Key, out output!);
    }

    // Compatibility wrappers for tooling/tests that still consume collection-shaped
    // results. The production keyboard path uses the Try APIs above.
    public IReadOnlyList<TOutput> OnKeyDown(KeyId key, long timestampMs)
        => TryOnKeyDown(key, timestampMs, out var output) ? [output] : [];

    public IReadOnlyList<TOutput> AdvanceTo(long timestampMs)
        => TryAdvanceTo(timestampMs, out var output) ? [output] : [];

    public IReadOnlyList<TOutput> Flush()
        => TryFlush(out var output) ? [output] : [];

    public void Cancel() => _pending = null;

    private void EnsureMonotonic(long timestampMs)
    {
        if (_pending is { } pending && timestampMs < pending.TimestampMs)
            throw new ArgumentOutOfRangeException(nameof(timestampMs), "Timestamps must be monotonic.");
    }

    private readonly record struct PendingKey(KeyId Key, long TimestampMs);
}
