using iKeyd.Core.Chords;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// Primitive effects emitted by behavior state machines. Standard behaviors and
/// future user-defined behaviors share this action vocabulary so helpers such as
/// LT/MT do not require dedicated execution branches in platform runtimes.
/// </summary>
public enum BehaviorActionKind
{
    SendKey,
    LayerOn,
    LayerOff,
    ModifierDown,
    ModifierUp
}

public readonly record struct BehaviorAction
{
    private BehaviorAction(BehaviorActionKind kind, KeyId key, string? name)
    {
        Kind = kind;
        Key = key;
        Name = name;
    }

    public BehaviorActionKind Kind { get; }
    public KeyId Key { get; }
    public string? Name { get; }

    public static BehaviorAction SendKey(KeyId key)
        => new(BehaviorActionKind.SendKey, key, null);

    public static BehaviorAction LayerOn(string layer)
        => new(BehaviorActionKind.LayerOn, default, RequireName(layer, nameof(layer)));

    public static BehaviorAction LayerOff(string layer)
        => new(BehaviorActionKind.LayerOff, default, RequireName(layer, nameof(layer)));

    public static BehaviorAction ModifierDown(string modifier)
        => new(BehaviorActionKind.ModifierDown, default, RequireName(modifier, nameof(modifier)));

    public static BehaviorAction ModifierUp(string modifier)
        => new(BehaviorActionKind.ModifierUp, default, RequireName(modifier, nameof(modifier)));

    private static string RequireName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

public readonly record struct BehaviorDispatchResult(
    bool Suppress,
    IReadOnlyList<BehaviorAction> Actions)
{
    public static BehaviorDispatchResult PassThrough { get; }
        = new(false, Array.Empty<BehaviorAction>());
}
