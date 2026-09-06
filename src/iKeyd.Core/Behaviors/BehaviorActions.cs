using iKeyd.Core.Chords;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// Primitive effects emitted by behavior state machines. Standard behaviors and
/// future user-defined behaviors share this action vocabulary so helpers such as
/// LT/MT and PC actions do not require dedicated execution branches per behavior.
/// </summary>
public enum BehaviorActionKind
{
    SendKey,
    SendText,
    LayerOn,
    LayerOff,
    ModifierDown,
    ModifierUp,
    MouseMove,
    MouseClick,
    Scroll,
    Media,
    Window,
    Clipboard,
    Macro
}

public readonly record struct BehaviorAction
{
    private BehaviorAction(BehaviorActionKind kind, KeyId key, string? name, int value1, int value2)
    {
        Kind = kind;
        Key = key;
        Name = name;
        Value1 = value1;
        Value2 = value2;
    }

    public BehaviorActionKind Kind { get; }
    public KeyId Key { get; }
    public string? Name { get; }
    public int Value1 { get; }
    public int Value2 { get; }

    public static BehaviorAction SendKey(KeyId key)
        => new(BehaviorActionKind.SendKey, key, null, 0, 0);

    public static BehaviorAction SendText(string text)
        => new(BehaviorActionKind.SendText, default, text ?? throw new ArgumentNullException(nameof(text)), 0, 0);

    public static BehaviorAction LayerOn(string layer)
        => Named(BehaviorActionKind.LayerOn, layer, nameof(layer));

    public static BehaviorAction LayerOff(string layer)
        => Named(BehaviorActionKind.LayerOff, layer, nameof(layer));

    public static BehaviorAction ModifierDown(string modifier)
        => Named(BehaviorActionKind.ModifierDown, modifier, nameof(modifier));

    public static BehaviorAction ModifierUp(string modifier)
        => Named(BehaviorActionKind.ModifierUp, modifier, nameof(modifier));

    public static BehaviorAction MouseMove(int deltaX, int deltaY)
        => new(BehaviorActionKind.MouseMove, default, null, deltaX, deltaY);

    public static BehaviorAction MouseClick(string button)
        => Named(BehaviorActionKind.MouseClick, button, nameof(button));

    public static BehaviorAction Scroll(int wheelDelta)
        => new(BehaviorActionKind.Scroll, default, null, wheelDelta, 0);

    public static BehaviorAction Media(string command)
        => Named(BehaviorActionKind.Media, command, nameof(command));

    public static BehaviorAction Window(string command)
        => Named(BehaviorActionKind.Window, command, nameof(command));

    public static BehaviorAction Clipboard(string command)
        => Named(BehaviorActionKind.Clipboard, command, nameof(command));

    public static BehaviorAction Macro(string template)
        => new(BehaviorActionKind.Macro, default, template ?? throw new ArgumentNullException(nameof(template)), 0, 0);

    private static BehaviorAction Named(BehaviorActionKind kind, string value, string parameterName)
        => new(kind, default, RequireName(value, parameterName), 0, 0);

    private static string RequireName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

public readonly record struct BehaviorDispatchResult(
    bool Suppress,
    IReadOnlyList<BehaviorAction> Actions)
{
    public static BehaviorDispatchResult PassThrough { get; }
        = new(false, Array.Empty<BehaviorAction>());
}
