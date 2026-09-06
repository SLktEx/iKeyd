using System.Buffers;
using System.Text;
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
    SendUnicode,
    SendText,
    LayerOn,
    LayerOff,
    ModifierDown,
    ModifierUp
}

/// <summary>
/// Describes whether an action is allowed to react to the platform's repeated
/// physical key-down events. The runtime does not synthesize its own repeat timer.
/// </summary>
public enum BehaviorRepeatPolicy
{
    Never,
    PhysicalKeyDown
}

public readonly record struct BehaviorAction
{
    private BehaviorAction(
        BehaviorActionKind kind,
        KeyId key,
        string? name,
        string? text,
        BehaviorRepeatPolicy repeatPolicy)
    {
        Kind = kind;
        Key = key;
        Name = name;
        Text = text;
        RepeatPolicy = repeatPolicy;
    }

    public BehaviorActionKind Kind { get; }
    public KeyId Key { get; }
    public string? Name { get; }
    public string? Text { get; }
    public BehaviorRepeatPolicy RepeatPolicy { get; }

    public static BehaviorAction SendKey(KeyId key)
        => new(BehaviorActionKind.SendKey, key, null, null, BehaviorRepeatPolicy.PhysicalKeyDown);

    public static BehaviorAction SendUnicode(string scalar)
        => new(
            BehaviorActionKind.SendUnicode,
            default,
            null,
            RequireUnicodeScalar(scalar, nameof(scalar)),
            BehaviorRepeatPolicy.PhysicalKeyDown);

    public static BehaviorAction SendText(string text)
        => new(
            BehaviorActionKind.SendText,
            default,
            null,
            RequireUnicodeText(text, nameof(text)),
            BehaviorRepeatPolicy.Never);

    public static BehaviorAction LayerOn(string layer)
        => new(BehaviorActionKind.LayerOn, default, RequireName(layer, nameof(layer)), null, BehaviorRepeatPolicy.Never);

    public static BehaviorAction LayerOff(string layer)
        => new(BehaviorActionKind.LayerOff, default, RequireName(layer, nameof(layer)), null, BehaviorRepeatPolicy.Never);

    public static BehaviorAction ModifierDown(string modifier)
        => new(BehaviorActionKind.ModifierDown, default, RequireName(modifier, nameof(modifier)), null, BehaviorRepeatPolicy.Never);

    public static BehaviorAction ModifierUp(string modifier)
        => new(BehaviorActionKind.ModifierUp, default, RequireName(modifier, nameof(modifier)), null, BehaviorRepeatPolicy.Never);

    private static string RequireName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string RequireUnicodeScalar(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0)
            throw new ArgumentException("Unicode output must contain exactly one Unicode scalar value.", parameterName);

        var status = Rune.DecodeFromUtf16(value.AsSpan(), out _, out var consumed);
        if (status != OperationStatus.Done || consumed != value.Length)
            throw new ArgumentException("Unicode output must contain exactly one valid Unicode scalar value.", parameterName);

        return value;
    }

    private static string RequireUnicodeText(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0)
            throw new ArgumentException("Text output must not be empty.", parameterName);

        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out _, out var consumed);
            if (status != OperationStatus.Done || consumed <= 0)
                throw new ArgumentException("Text output must contain only valid Unicode scalar values.", parameterName);
            remaining = remaining[consumed..];
        }

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
