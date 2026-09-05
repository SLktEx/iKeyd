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

public sealed class LayerState : IEquatable<LayerState>
{
    private readonly LayerKey[] _order;

    private LayerState(LayerKey[] order)
    {
        _order = order;
        var modifiers = LayerModifiers.None;
        foreach (var key in order)
            modifiers |= ToModifier(key);
        Modifiers = modifiers;
    }

    public static LayerState Empty { get; } = new([]);

    public LayerModifiers Modifiers { get; }
    public int Count => _order.Length;

    public static LayerState FromSequence(params LayerKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var state = Empty;
        foreach (var key in keys)
            state = state.Press(key);
        return state;
    }

    public bool Contains(LayerKey key) => (Modifiers & ToModifier(key)) != 0;

    public bool IsExact(params LayerKey[] keys)
    {
        if (keys.Length != _order.Length)
            return false;

        for (var i = 0; i < keys.Length; i++)
        {
            if (_order[i] != keys[i])
                return false;
        }

        return true;
    }

    public LayerState Press(LayerKey key)
    {
        if (Contains(key))
            return this;

        var next = new LayerKey[_order.Length + 1];
        Array.Copy(_order, next, _order.Length);
        next[^1] = key;
        return new LayerState(next);
    }

    public LayerState Release(params LayerKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length == 0 || _order.Length == 0)
            return this;

        var remove = new HashSet<LayerKey>(keys);
        var next = _order.Where(key => !remove.Contains(key)).ToArray();
        return next.Length == _order.Length ? this : new LayerState(next);
    }

    public override string ToString() => string.Concat(_order.Select(ToLegacyCode));

    public bool Equals(LayerState? other)
        => other is not null && _order.SequenceEqual(other._order);

    public override bool Equals(object? obj) => obj is LayerState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var key in _order)
            hash.Add(key);
        return hash.ToHashCode();
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

    private static string ToLegacyCode(LayerKey key) => key switch
    {
        LayerKey.M => "M",
        LayerKey.H => "H",
        LayerKey.S => "S",
        LayerKey.K => "K",
        LayerKey.A => "A",
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
}
