using System.Buffers;
using System.Text;
using iKeyd.Core.Automation;
using iKeyd.Core.Chords;
using iKeyd.Core.State;

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
    LayerToggle,
    LayerSet,
    ModifierDown,
    ModifierUp,
    Exec,
    Shell,
    Query,
    StateSet,
    StateToggle
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
    private static readonly IReadOnlyList<string> EmptyArguments = Array.Empty<string>();

    private BehaviorAction(
        BehaviorActionKind kind,
        KeyId key,
        string? name,
        string? text,
        IReadOnlyList<string>? arguments,
        BehaviorRepeatPolicy repeatPolicy)
    {
        Kind = kind;
        Key = key;
        Name = name;
        Text = text;
        Arguments = arguments ?? EmptyArguments;
        RepeatPolicy = repeatPolicy;
    }

    public BehaviorActionKind Kind { get; }
    public KeyId Key { get; }
    public string? Name { get; }
    public string? Text { get; }
    public IReadOnlyList<string> Arguments { get; }
    public BehaviorRepeatPolicy RepeatPolicy { get; }

    public static BehaviorAction SendKey(KeyId key)
        => new(BehaviorActionKind.SendKey, key, null, null, null, BehaviorRepeatPolicy.PhysicalKeyDown);

    public static BehaviorAction SendUnicode(string scalar)
        => new(
            BehaviorActionKind.SendUnicode,
            default,
            null,
            RequireUnicodeScalar(scalar, nameof(scalar)),
            null,
            BehaviorRepeatPolicy.PhysicalKeyDown);

    public static BehaviorAction SendText(string text)
        => new(
            BehaviorActionKind.SendText,
            default,
            null,
            RequireUnicodeText(text, nameof(text)),
            null,
            BehaviorRepeatPolicy.Never);

    public static BehaviorAction LayerOn(string layer)
        => new(BehaviorActionKind.LayerOn, default, RequireName(layer, nameof(layer)), null, null, BehaviorRepeatPolicy.Never);

    public static BehaviorAction LayerOff(string layer)
        => new(BehaviorActionKind.LayerOff, default, RequireName(layer, nameof(layer)), null, null, BehaviorRepeatPolicy.Never);

    /// <summary>
    /// Toggles persistent membership of a layer. This is deliberately separate
    /// from LayerOn/LayerOff ownership so a TG-style latch cannot consume or leak
    /// a momentary MO/LT activation.
    /// </summary>
    public static BehaviorAction LayerToggle(string layer)
        => new(BehaviorActionKind.LayerToggle, default, RequireName(layer, nameof(layer)), null, null, BehaviorRepeatPolicy.Never);

    /// <summary>
    /// Replaces the persistent layer selection with one layer. Momentary owned
    /// layers remain independently owned until their source behavior releases.
    /// </summary>
    public static BehaviorAction LayerSet(string layer)
        => new(BehaviorActionKind.LayerSet, default, RequireName(layer, nameof(layer)), null, null, BehaviorRepeatPolicy.Never);

    public static BehaviorAction ModifierDown(string modifier)
        => new(BehaviorActionKind.ModifierDown, default, RequireName(modifier, nameof(modifier)), null, null, BehaviorRepeatPolicy.Never);

    public static BehaviorAction ModifierUp(string modifier)
        => new(BehaviorActionKind.ModifierUp, default, RequireName(modifier, nameof(modifier)), null, null, BehaviorRepeatPolicy.Never);

    public static BehaviorAction Exec(string executable, IEnumerable<string>? arguments = null)
    {
        var request = CommandRequest.Exec(executable, arguments);
        return new(
            BehaviorActionKind.Exec,
            default,
            request.Command,
            null,
            request.Arguments,
            BehaviorRepeatPolicy.Never);
    }

    public static BehaviorAction Shell(string command)
    {
        var request = CommandRequest.Shell(command);
        return new(
            BehaviorActionKind.Shell,
            default,
            null,
            request.Command,
            null,
            BehaviorRepeatPolicy.Never);
    }

    public static BehaviorAction Query(string key)
        => new(
            BehaviorActionKind.Query,
            default,
            SystemQueryKeys.Normalize(key),
            null,
            null,
            BehaviorRepeatPolicy.Never);

    public static BehaviorAction StateSet(string fieldName, string value)
        => new(
            BehaviorActionKind.StateSet,
            default,
            RuntimeStateProfile.NormalizeName(fieldName),
            value ?? throw new ArgumentNullException(nameof(value)),
            null,
            BehaviorRepeatPolicy.Never);

    public static BehaviorAction StateToggle(string fieldName)
        => new(
            BehaviorActionKind.StateToggle,
            default,
            RuntimeStateProfile.NormalizeName(fieldName),
            null,
            null,
            BehaviorRepeatPolicy.Never);

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
