using iKeyd.Core.Chords;

namespace iKeyd.Core.Keymaps;

public sealed class Keymap<TOutput> where TOutput : notnull
{
    private readonly Dictionary<KeyId, TOutput> _single = [];
    private readonly Dictionary<ChordKey, TOutput> _chords = [];

    public Keymap(
        IEnumerable<SingleMapping<TOutput>> singleMappings,
        IEnumerable<ChordMapping<TOutput>> chordMappings)
    {
        ArgumentNullException.ThrowIfNull(singleMappings);
        ArgumentNullException.ThrowIfNull(chordMappings);

        // AHK variable assignment is last-write-wins for single-stroke mappings.
        foreach (var mapping in singleMappings)
            _single[mapping.Key] = mapping.Output;

        // The legacy chord lookup scans declarations from the beginning, so the
        // first declaration wins when the same unordered key pair is repeated.
        foreach (var mapping in chordMappings)
            _chords.TryAdd(new ChordKey(mapping.First, mapping.Second), mapping.Output);
    }

    public bool TryGetSingle(KeyId key, out TOutput output)
    {
        if (_single.TryGetValue(key, out var value))
        {
            output = value;
            return true;
        }

        output = default!;
        return false;
    }

    public bool TryGetChord(KeyId first, KeyId second, out TOutput output)
    {
        if (_chords.TryGetValue(new ChordKey(first, second), out var value))
        {
            output = value;
            return true;
        }

        output = default!;
        return false;
    }
}
