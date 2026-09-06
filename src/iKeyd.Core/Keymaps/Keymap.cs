using iKeyd.Core.Chords;

namespace iKeyd.Core.Keymaps;

public readonly struct KeymapSlot<TOutput> where TOutput : notnull
{
    private readonly TOutput? _value;

    public KeymapSlot(TOutput value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        HasValue = true;
    }

    public bool HasValue { get; }

    public bool TryGet(out TOutput output)
    {
        if (HasValue)
        {
            output = _value!;
            return true;
        }

        output = default!;
        return false;
    }
}

public sealed class Keymap<TOutput> where TOutput : notnull
{
    // Compact KeyCode values are dense and start at 1. Keep the lookup tables
    // sized from the enum boundary so changing the compact key universe is an
    // explicit source change rather than a runtime discovery step.
    public const int CompactKeyCount = (int)KeyCode.NumpadComma;
    public const int CompactSingleSlotCount = CompactKeyCount;
    public const int CompactChordSlotCount = CompactKeyCount * (CompactKeyCount + 1) / 2;

    private readonly KeymapSlot<TOutput>[] _single;
    private readonly KeymapSlot<TOutput>[] _chords;
    private readonly Dictionary<KeyId, TOutput>? _customSingle;
    private readonly Dictionary<ChordKey, TOutput>? _customChords;

    public Keymap(
        IEnumerable<SingleMapping<TOutput>> singleMappings,
        IEnumerable<ChordMapping<TOutput>> chordMappings)
    {
        ArgumentNullException.ThrowIfNull(singleMappings);
        ArgumentNullException.ThrowIfNull(chordMappings);

        _single = new KeymapSlot<TOutput>[CompactSingleSlotCount];
        _chords = new KeymapSlot<TOutput>[CompactChordSlotCount];

        Dictionary<KeyId, TOutput>? customSingle = null;
        Dictionary<ChordKey, TOutput>? customChords = null;

        // AHK variable assignment is last-write-wins for single-stroke mappings.
        foreach (var mapping in singleMappings)
        {
            if (mapping.Key.IsCompact)
            {
                _single[GetCompactSingleIndex(mapping.Key.Code)] = new KeymapSlot<TOutput>(mapping.Output);
            }
            else
            {
                customSingle ??= [];
                customSingle[mapping.Key] = mapping.Output;
            }
        }

        // The legacy chord lookup scans declarations from the beginning, so the
        // first declaration wins when the same unordered key pair is repeated.
        foreach (var mapping in chordMappings)
        {
            if (mapping.First.IsCompact && mapping.Second.IsCompact)
            {
                var index = GetCompactChordIndex(mapping.First.Code, mapping.Second.Code);
                if (!_chords[index].HasValue)
                    _chords[index] = new KeymapSlot<TOutput>(mapping.Output);
            }
            else
            {
                customChords ??= [];
                customChords.TryAdd(new ChordKey(mapping.First, mapping.Second), mapping.Output);
            }
        }

        _customSingle = customSingle;
        _customChords = customChords;
    }

    private Keymap(KeymapSlot<TOutput>[] single, KeymapSlot<TOutput>[] chords)
    {
        ArgumentNullException.ThrowIfNull(single);
        ArgumentNullException.ThrowIfNull(chords);
        if (single.Length != CompactSingleSlotCount)
            throw new ArgumentException($"Compiled single-key table must contain exactly {CompactSingleSlotCount} slots.", nameof(single));
        if (chords.Length != CompactChordSlotCount)
            throw new ArgumentException($"Compiled chord table must contain exactly {CompactChordSlotCount} slots.", nameof(chords));

        _single = single;
        _chords = chords;
    }

    /// <summary>
    /// Creates a keymap from lookup-ready tables emitted by the build-time profile
    /// compiler. Ownership of the arrays transfers to the keymap; callers must not
    /// mutate them after this call.
    /// </summary>
    public static Keymap<TOutput> FromCompiledTables(
        KeymapSlot<TOutput>[] single,
        KeymapSlot<TOutput>[] chords)
        => new(single, chords);

    public bool TryGetSingle(KeyId key, out TOutput output)
    {
        if (key.IsCompact)
            return _single[GetCompactSingleIndex(key.Code)].TryGet(out output);

        if (_customSingle is not null && _customSingle.TryGetValue(key, out var value))
        {
            output = value;
            return true;
        }

        output = default!;
        return false;
    }

    public bool TryGetChord(KeyId first, KeyId second, out TOutput output)
    {
        if (first.IsCompact && second.IsCompact)
            return _chords[GetCompactChordIndex(first.Code, second.Code)].TryGet(out output);

        if (_customChords is not null && _customChords.TryGetValue(new ChordKey(first, second), out var value))
        {
            output = value;
            return true;
        }

        output = default!;
        return false;
    }

    public static int GetCompactSingleIndex(KeyCode code)
    {
        var value = (int)code;
        if (value < (int)KeyCode.A || value > CompactKeyCount)
            throw new ArgumentOutOfRangeException(nameof(code), code, "Key code is not compact.");
        return value - 1;
    }

    public static int GetCompactChordIndex(KeyCode first, KeyCode second)
    {
        var firstIndex = GetCompactSingleIndex(first);
        var secondIndex = GetCompactSingleIndex(second);
        if (firstIndex > secondIndex)
            (firstIndex, secondIndex) = (secondIndex, firstIndex);

        // Flatten the upper triangle, including its diagonal. For N compact keys,
        // row x starts after x*N - x*(x-1)/2 preceding entries.
        var rowOffset = firstIndex * CompactKeyCount - firstIndex * (firstIndex - 1) / 2;
        return rowOffset + secondIndex - firstIndex;
    }
}
