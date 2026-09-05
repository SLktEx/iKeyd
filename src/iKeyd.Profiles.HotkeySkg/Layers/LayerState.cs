namespace iKeyd.Profiles.HotkeySkg.Layers;

[Flags]
public enum LayerModifiers
{
    None = 0,
    M = 1 << 0,
    H = 1 << 1,
    S = 1 << 2,
    K = 1 << 3,
    A = 1 << 4
}

public enum LayerKey
{
    M,
    H,
    S,
    K,
    A
}

/// <summary>
/// Allocation-free ordered layer state. At most five distinct layer keys can be
/// pressed, so their order fits in 15 bits using three bits per key.
/// </summary>
public readonly struct LayerState : IEquatable<LayerState>
{
    private readonly ushort _orderBits;

    private LayerState(ushort orderBits, byte count, LayerModifiers modifiers)
    {
        _orderBits = orderBits;
        Count = count;
        Modifiers = modifiers;
    }

    public static LayerState Empty => default;

    public LayerModifiers Modifiers { get; }
    public byte Count { get; }

    public static LayerState FromSequence(params LayerKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var state = Empty;
        foreach (var key in keys)
            state = state.Press(key);
        return state;
    }

    public bool Contains(LayerKey key) => (Modifiers & ToModifier(key)) != 0;

    public bool IsExact(LayerKey first)
        => Count == 1 && GetAt(0) == first;

    public bool IsExact(LayerKey first, LayerKey second)
        => Count == 2 && GetAt(0) == first && GetAt(1) == second;

    public bool IsExact(LayerKey first, LayerKey second, LayerKey third)
        => Count == 3 && GetAt(0) == first && GetAt(1) == second && GetAt(2) == third;

    public bool IsExact(params LayerKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length != Count)
            return false;

        for (var i = 0; i < keys.Length; i++)
        {
            if (GetAt(i) != keys[i])
                return false;
        }

        return true;
    }

    public LayerState Press(LayerKey key)
    {
        if (Contains(key))
            return this;
        if (Count >= 5)
            throw new InvalidOperationException("All layer keys are already represented in the state.");

        var token = (ushort)((int)key + 1);
        var nextBits = (ushort)(_orderBits | (token << (Count * 3)));
        return new LayerState(nextBits, (byte)(Count + 1), Modifiers | ToModifier(key));
    }

    public LayerState Release(LayerKey first)
        => ReleaseCore(ToModifier(first));

    public LayerState Release(LayerKey first, LayerKey second)
        => ReleaseCore(ToModifier(first) | ToModifier(second));

    public LayerState Release(LayerKey first, LayerKey second, LayerKey third)
        => ReleaseCore(ToModifier(first) | ToModifier(second) | ToModifier(third));

    public LayerState Release(params LayerKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var remove = LayerModifiers.None;
        foreach (var key in keys)
            remove |= ToModifier(key);
        return ReleaseCore(remove);
    }

    public override string ToString()
    {
        if (Count == 0)
            return string.Empty;

        Span<char> buffer = stackalloc char[5];
        for (var i = 0; i < Count; i++)
            buffer[i] = ToLegacyCode(GetAt(i));
        return new string(buffer[..Count]);
    }

    public bool Equals(LayerState other)
        => _orderBits == other._orderBits && Count == other.Count;

    public override bool Equals(object? obj) => obj is LayerState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_orderBits, Count);

    public static bool operator ==(LayerState left, LayerState right) => left.Equals(right);
    public static bool operator !=(LayerState left, LayerState right) => !left.Equals(right);

    private LayerState ReleaseCore(LayerModifiers remove)
    {
        if (remove == LayerModifiers.None || Count == 0 || (Modifiers & remove) == 0)
            return this;

        var next = Empty;
        for (var i = 0; i < Count; i++)
        {
            var key = GetAt(i);
            if ((remove & ToModifier(key)) == 0)
                next = next.Press(key);
        }
        return next;
    }

    private LayerKey GetAt(int index)
    {
        if ((uint)index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var token = (_orderBits >> (index * 3)) & 0b111;
        return (LayerKey)(token - 1);
    }

    private static LayerModifiers ToModifier(LayerKey key) => key switch
    {
        LayerKey.M => LayerModifiers.M,
        LayerKey.H => LayerModifiers.H,
        LayerKey.S => LayerModifiers.S,
        LayerKey.K => LayerModifiers.K,
        LayerKey.A => LayerModifiers.A,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static char ToLegacyCode(LayerKey key) => key switch
    {
        LayerKey.M => 'M',
        LayerKey.H => 'H',
        LayerKey.S => 'S',
        LayerKey.K => 'K',
        LayerKey.A => 'A',
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
}
