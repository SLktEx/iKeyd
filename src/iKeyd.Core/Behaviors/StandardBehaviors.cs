using iKeyd.Core.Chords;

namespace iKeyd.Core.Behaviors;

public sealed record LayerTapOptions
{
    public const int DefaultTappingTermMs = 200;

    public int TappingTermMs { get; init; } = DefaultTappingTermMs;
    public bool HoldOnOtherKeyPress { get; init; } = true;
}

public sealed record ModTapOptions
{
    public const int DefaultTappingTermMs = LayerTapOptions.DefaultTappingTermMs;

    public int TappingTermMs { get; init; } = DefaultTappingTermMs;
    public bool HoldOnOtherKeyPress { get; init; } = true;
}

/// <summary>
/// Standard behavior library. These helpers create ordinary behavior definitions;
/// the runtime has no LT/MT-specific dispatch path.
/// </summary>
public static class StandardBehaviors
{
    public static BehaviorDefinition LT(
        string layer,
        KeyId tapKey,
        LayerTapOptions? options = null)
        => new LayerTapBehaviorDefinition(layer, tapKey, options ?? new LayerTapOptions());

    public static BehaviorDefinition MT(
        string modifier,
        KeyId tapKey,
        ModTapOptions? options = null)
        => new ModTapBehaviorDefinition(modifier, tapKey, options ?? new ModTapOptions());
}

internal sealed class LayerTapBehaviorDefinition : BehaviorDefinition
{
    private readonly string _layer;
    private readonly KeyId _tapKey;
    private readonly LayerTapOptions _options;

    public LayerTapBehaviorDefinition(string layer, KeyId tapKey, LayerTapOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layer);
        ArgumentNullException.ThrowIfNull(options);
        ValidateTappingTerm(options.TappingTermMs, nameof(options));

        _layer = layer;
        _tapKey = tapKey;
        _options = options;
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new LayerTapBehaviorInstance(sourceKey, _layer, _tapKey, _options, timestampMs);

    private static void ValidateTappingTerm(int tappingTermMs, string parameterName)
    {
        if (tappingTermMs < 0)
            throw new ArgumentOutOfRangeException(parameterName, "Tapping term must be non-negative.");
    }
}

internal sealed class ModTapBehaviorDefinition : BehaviorDefinition
{
    private readonly string _modifier;
    private readonly KeyId _tapKey;
    private readonly ModTapOptions _options;

    public ModTapBehaviorDefinition(string modifier, KeyId tapKey, ModTapOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modifier);
        ArgumentNullException.ThrowIfNull(options);
        if (options.TappingTermMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Tapping term must be non-negative.");

        _modifier = modifier;
        _tapKey = tapKey;
        _options = options;
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new ModTapBehaviorInstance(sourceKey, _modifier, _tapKey, _options, timestampMs);
}

internal abstract class TapHoldBehaviorInstance : BehaviorInstance
{
    private enum Resolution
    {
        Pending,
        Hold,
        Released
    }

    private readonly KeyId _tapKey;
    private readonly int _tappingTermMs;
    private readonly bool _holdOnOtherKeyPress;
    private readonly long _pressedAtMs;
    private Resolution _resolution;

    protected TapHoldBehaviorInstance(
        KeyId sourceKey,
        KeyId tapKey,
        int tappingTermMs,
        bool holdOnOtherKeyPress,
        long pressedAtMs)
        : base(sourceKey)
    {
        _tapKey = tapKey;
        _tappingTermMs = tappingTermMs;
        _holdOnOtherKeyPress = holdOnOtherKeyPress;
        _pressedAtMs = pressedAtMs;
    }

    protected abstract BehaviorAction HoldDownAction { get; }
    protected abstract BehaviorAction HoldUpAction { get; }

    internal override void AdvanceTo(long timestampMs, List<BehaviorAction> actions)
    {
        if (_resolution != Resolution.Pending)
            return;

        if (timestampMs - _pressedAtMs >= _tappingTermMs)
            ResolveHold(actions);
    }

    internal override void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
    {
        if (_resolution == Resolution.Pending && _holdOnOtherKeyPress)
            ResolveHold(actions);
    }

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
        switch (_resolution)
        {
            case Resolution.Pending:
                actions.Add(BehaviorAction.SendKey(_tapKey));
                break;
            case Resolution.Hold:
                actions.Add(HoldUpAction);
                break;
            case Resolution.Released:
                return;
        }

        _resolution = Resolution.Released;
    }

    internal override void Cancel(List<BehaviorAction> actions)
    {
        if (_resolution == Resolution.Hold)
            actions.Add(HoldUpAction);
        _resolution = Resolution.Released;
    }

    private void ResolveHold(List<BehaviorAction> actions)
    {
        _resolution = Resolution.Hold;
        actions.Add(HoldDownAction);
    }
}

internal sealed class LayerTapBehaviorInstance : TapHoldBehaviorInstance
{
    private readonly string _layer;

    public LayerTapBehaviorInstance(
        KeyId sourceKey,
        string layer,
        KeyId tapKey,
        LayerTapOptions options,
        long pressedAtMs)
        : base(sourceKey, tapKey, options.TappingTermMs, options.HoldOnOtherKeyPress, pressedAtMs)
    {
        _layer = layer;
    }

    protected override BehaviorAction HoldDownAction => BehaviorAction.LayerOn(_layer);
    protected override BehaviorAction HoldUpAction => BehaviorAction.LayerOff(_layer);
}

internal sealed class ModTapBehaviorInstance : TapHoldBehaviorInstance
{
    private readonly string _modifier;

    public ModTapBehaviorInstance(
        KeyId sourceKey,
        string modifier,
        KeyId tapKey,
        ModTapOptions options,
        long pressedAtMs)
        : base(sourceKey, tapKey, options.TappingTermMs, options.HoldOnOtherKeyPress, pressedAtMs)
    {
        _modifier = modifier;
    }

    protected override BehaviorAction HoldDownAction => BehaviorAction.ModifierDown(_modifier);
    protected override BehaviorAction HoldUpAction => BehaviorAction.ModifierUp(_modifier);
}
