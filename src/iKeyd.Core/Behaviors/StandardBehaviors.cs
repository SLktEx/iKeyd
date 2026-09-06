using iKeyd.Core.Chords;

namespace iKeyd.Core.Behaviors;

public sealed record LayerTapOptions
{
    public const int DefaultTappingTermMs = 200;

    public int TappingTermMs { get; init; } = DefaultTappingTermMs;
    public bool HoldOnOtherKeyPress { get; init; } = true;
}

/// <summary>
/// Standard behavior library. These helpers create ordinary behavior definitions;
/// the runtime has no LT-specific dispatch path.
/// </summary>
public static class StandardBehaviors
{
    public static BehaviorDefinition LT(
        string layer,
        KeyId tapKey,
        LayerTapOptions? options = null)
        => new LayerTapBehaviorDefinition(layer, tapKey, options ?? new LayerTapOptions());
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
        if (options.TappingTermMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Tapping term must be non-negative.");

        _layer = layer;
        _tapKey = tapKey;
        _options = options;
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new LayerTapBehaviorInstance(sourceKey, _layer, _tapKey, _options, timestampMs);
}

internal sealed class LayerTapBehaviorInstance : BehaviorInstance
{
    private enum Resolution
    {
        Pending,
        Hold,
        Released
    }

    private readonly string _layer;
    private readonly KeyId _tapKey;
    private readonly LayerTapOptions _options;
    private readonly long _pressedAtMs;
    private Resolution _resolution;

    public LayerTapBehaviorInstance(
        KeyId sourceKey,
        string layer,
        KeyId tapKey,
        LayerTapOptions options,
        long pressedAtMs)
        : base(sourceKey)
    {
        _layer = layer;
        _tapKey = tapKey;
        _options = options;
        _pressedAtMs = pressedAtMs;
    }

    internal override void AdvanceTo(long timestampMs, List<BehaviorAction> actions)
    {
        if (_resolution != Resolution.Pending)
            return;

        if (timestampMs - _pressedAtMs >= _options.TappingTermMs)
            ResolveHold(actions);
    }

    internal override void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
    {
        if (_resolution == Resolution.Pending && _options.HoldOnOtherKeyPress)
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
                actions.Add(BehaviorAction.LayerOff(_layer));
                break;
            case Resolution.Released:
                return;
        }

        _resolution = Resolution.Released;
    }

    internal override void Cancel(List<BehaviorAction> actions)
    {
        if (_resolution == Resolution.Hold)
            actions.Add(BehaviorAction.LayerOff(_layer));
        _resolution = Resolution.Released;
    }

    private void ResolveHold(List<BehaviorAction> actions)
    {
        _resolution = Resolution.Hold;
        actions.Add(BehaviorAction.LayerOn(_layer));
    }
}
