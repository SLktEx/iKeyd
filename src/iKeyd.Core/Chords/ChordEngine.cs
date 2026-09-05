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

    public IReadOnlyList<TOutput> OnKeyDown(KeyId key, long timestampMs)
    {
        if (_pending is null)
        {
            _pending = new PendingKey(key, timestampMs);
            return [];
        }

        EnsureMonotonic(timestampMs);

        var pending = _pending.Value;
        var elapsed = timestampMs - pending.TimestampMs;

        if (elapsed <= _chordWindowMs && _keymap.TryGetChord(pending.Key, key, out var chordOutput))
        {
            _pending = null;
            return [chordOutput];
        }

        var outputs = ResolveSingle(pending.Key);
        _pending = new PendingKey(key, timestampMs);
        return outputs;
    }

    public IReadOnlyList<TOutput> AdvanceTo(long timestampMs)
    {
        if (_pending is null)
            return [];

        EnsureMonotonic(timestampMs);

        // The legacy chord condition is inclusive (<= 40 ms). Keeping the
        // pending key at exactly the boundary allows a key-down at 40 ms to be
        // processed before timeout expiry in a deterministic event loop.
        if (timestampMs - _pending.Value.TimestampMs <= _chordWindowMs)
            return [];

        var pending = _pending.Value;
        _pending = null;
        return ResolveSingle(pending.Key);
    }

    public IReadOnlyList<TOutput> Flush()
    {
        if (_pending is null)
            return [];

        var pending = _pending.Value;
        _pending = null;
        return ResolveSingle(pending.Key);
    }

    public void Cancel() => _pending = null;

    private IReadOnlyList<TOutput> ResolveSingle(KeyId key)
        => _keymap.TryGetSingle(key, out var output) ? [output] : [];

    private void EnsureMonotonic(long timestampMs)
    {
        if (_pending is { } pending && timestampMs < pending.TimestampMs)
            throw new ArgumentOutOfRangeException(nameof(timestampMs), "Timestamps must be monotonic.");
    }

    private readonly record struct PendingKey(KeyId Key, long TimestampMs);
}
