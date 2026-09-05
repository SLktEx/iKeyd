namespace iKeyd.Core.Chords;

public readonly record struct KeyId
{
    public KeyId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Key id must not be empty.", nameof(value));

        Value = value.Trim().ToUpperInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator KeyId(string value) => new(value);
}

public readonly record struct ChordKey
{
    public ChordKey(KeyId first, KeyId second)
    {
        if (string.CompareOrdinal(first.Value, second.Value) <= 0)
        {
            First = first;
            Second = second;
        }
        else
        {
            First = second;
            Second = first;
        }
    }

    public KeyId First { get; }
    public KeyId Second { get; }
}

public sealed record SingleMapping<TOutput>(KeyId Key, TOutput Output) where TOutput : notnull;
public sealed record ChordMapping<TOutput>(KeyId First, KeyId Second, TOutput Output) where TOutput : notnull;
