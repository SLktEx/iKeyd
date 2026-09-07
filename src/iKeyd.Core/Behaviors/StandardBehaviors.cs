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

public sealed record TapDanceOptions
{
    public const int DefaultTappingTermMs = LayerTapOptions.DefaultTappingTermMs;
    public const int MaxTapCount = 8;

    public int TappingTermMs { get; init; } = DefaultTappingTermMs;
}

/// <summary>
/// Standard behavior library. These helpers create ordinary behavior definitions;
/// the runtime has no helper-name-specific dispatch path.
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

    public static BehaviorDefinition MO(string layer)
        => new MomentaryLayerBehaviorDefinition(layer);

    public static BehaviorDefinition MOD(string modifier)
        => new ModifierHoldBehaviorDefinition(modifier);

    /// <summary>
    /// Toggles a persistent layer selection. Physical auto-repeat cannot replay
    /// the toggle because layer-state actions use RepeatPolicy.Never.
    /// </summary>
    public static BehaviorDefinition TG(string layer)
        => Press(BehaviorAction.LayerToggle(layer));

    /// <summary>
    /// Replaces the persistent layer selection with one layer. Momentary layer
    /// ownership remains independent and is released by its owning behavior.
    /// </summary>
    public static BehaviorDefinition TO(string layer)
        => Press(BehaviorAction.LayerSet(layer));

    /// <summary>
    /// While held, acts as a normal momentary layer. A clean tap arms the layer
    /// for exactly the next supported physical key lifecycle.
    /// </summary>
    public static BehaviorDefinition OSL(string layer)
        => new OneShotLayerBehaviorDefinition(layer);

    /// <summary>
    /// While held, acts as a normal modifier. A clean tap arms the modifier for
    /// exactly the next supported physical key lifecycle.
    /// </summary>
    public static BehaviorDefinition OSM(string modifier)
        => new OneShotModifierBehaviorDefinition(modifier);

    /// <summary>
    /// Bounded tap dance. The first key is emitted for one tap, the second for two
    /// taps, and so on. The sequence may remain alive after release only until its
    /// finite inter-tap deadline.
    /// </summary>
    public static BehaviorDefinition TD(
        IEnumerable<KeyId> tapKeys,
        TapDanceOptions? options = null)
        => new TapDanceBehaviorDefinition(tapKeys, options ?? new TapDanceOptions());

    public static BehaviorDefinition Unicode(string scalar)
        => Press(BehaviorAction.SendUnicode(scalar));

    public static BehaviorDefinition Text(string text)
        => Press(BehaviorAction.SendText(text));

    /// <summary>
    /// Emits a primitive output on first physical down. If the primitive declares
    /// physical-key repeat support, repeated downs emit it again; otherwise they
    /// remain suppressed without replaying the action.
    /// </summary>
    public static BehaviorDefinition Press(BehaviorAction action)
        => new PressActionBehaviorDefinition(action);
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

    internal override long? NextDeadlineMs
        => _resolution == Resolution.Pending
            ? _pressedAtMs + _tappingTermMs
            : null;

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

internal sealed class TapDanceBehaviorDefinition : BehaviorDefinition
{
    private readonly KeyId[] _tapKeys;
    private readonly TapDanceOptions _options;

    public TapDanceBehaviorDefinition(IEnumerable<KeyId> tapKeys, TapDanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(tapKeys);
        ArgumentNullException.ThrowIfNull(options);

        _tapKeys = tapKeys.ToArray();
        if (_tapKeys.Length < 2 || _tapKeys.Length > TapDanceOptions.MaxTapCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tapKeys),
                $"Tap dance requires between 2 and {TapDanceOptions.MaxTapCount} outputs.");
        }
        if (options.TappingTermMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Tapping term must be non-negative.");

        _options = options;
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new TapDanceBehaviorInstance(sourceKey, _tapKeys, _options.TappingTermMs);
}

internal sealed class TapDanceBehaviorInstance(
    KeyId sourceKey,
    IReadOnlyList<KeyId> tapKeys,
    int tappingTermMs) : BehaviorInstance(sourceKey)
{
    private int _tapCount;
    private bool _pressed;
    private bool _resolved;
    private long? _deadlineMs;

    internal override bool KeepAliveAfterRelease
        => !_pressed && !_resolved && _tapCount > 0 && _deadlineMs is not null;

    internal override long? NextDeadlineMs
        => KeepAliveAfterRelease ? _deadlineMs : null;

    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
    {
        if (_resolved)
            return;

        _pressed = true;
        _deadlineMs = null;
        _tapCount++;
    }

    internal override void AdvanceTo(long timestampMs, List<BehaviorAction> actions)
    {
        if (!KeepAliveAfterRelease || _deadlineMs is not long deadline || timestampMs < deadline)
            return;

        Resolve(actions);
    }

    internal override void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
    {
        if (!_resolved && _tapCount > 0)
            Resolve(actions);
    }

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
        if (!_pressed)
            return;

        _pressed = false;
        if (_resolved)
            return;

        if (_tapCount >= tapKeys.Count || tappingTermMs == 0)
        {
            Resolve(actions);
            return;
        }

        _deadlineMs = timestampMs + tappingTermMs;
    }

    internal override void Cancel(List<BehaviorAction> actions)
    {
        _pressed = false;
        _resolved = true;
        _deadlineMs = null;
    }

    private void Resolve(List<BehaviorAction> actions)
    {
        if (_resolved || _tapCount <= 0)
            return;

        var outputIndex = Math.Clamp(_tapCount, 1, tapKeys.Count) - 1;
        actions.Add(BehaviorAction.SendKey(tapKeys[outputIndex]));
        _resolved = true;
        _deadlineMs = null;
    }
}

internal sealed class MomentaryLayerBehaviorDefinition : BehaviorDefinition
{
    private readonly string _layer;

    public MomentaryLayerBehaviorDefinition(string layer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layer);
        _layer = layer.Trim();
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new MomentaryLayerBehaviorInstance(sourceKey, _layer);
}

internal sealed class MomentaryLayerBehaviorInstance(KeyId sourceKey, string layer) : BehaviorInstance(sourceKey)
{
    private bool _active;

    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
    {
        _active = true;
        actions.Add(BehaviorAction.LayerOn(layer));
    }

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions) => Release(actions);
    internal override void Cancel(List<BehaviorAction> actions) => Release(actions);

    private void Release(List<BehaviorAction> actions)
    {
        if (!_active)
            return;

        _active = false;
        actions.Add(BehaviorAction.LayerOff(layer));
    }
}

internal sealed class OneShotLayerBehaviorDefinition : BehaviorDefinition
{
    private readonly string _layer;

    public OneShotLayerBehaviorDefinition(string layer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layer);
        _layer = layer.Trim();
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new OneShotLayerBehaviorInstance(sourceKey, _layer);
}

internal sealed class OneShotLayerBehaviorInstance(KeyId sourceKey, string layer) : BehaviorInstance(sourceKey)
{
    private bool _active;
    private bool _interrupted;

    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
    {
        _active = true;
        actions.Add(BehaviorAction.LayerOn(layer));
    }

    internal override void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
        => _interrupted = true;

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
        if (!_active)
            return;

        _active = false;
        actions.Add(BehaviorAction.LayerOff(layer));
        if (!_interrupted)
            actions.Add(BehaviorAction.LayerOneShot(layer));
    }

    internal override void Cancel(List<BehaviorAction> actions)
    {
        if (!_active)
            return;

        _active = false;
        actions.Add(BehaviorAction.LayerOff(layer));
    }
}

internal sealed class ModifierHoldBehaviorDefinition : BehaviorDefinition
{
    private readonly string _modifier;

    public ModifierHoldBehaviorDefinition(string modifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modifier);
        _modifier = modifier.Trim();
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new ModifierHoldBehaviorInstance(sourceKey, _modifier);
}

internal sealed class ModifierHoldBehaviorInstance(KeyId sourceKey, string modifier) : BehaviorInstance(sourceKey)
{
    private bool _active;

    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
    {
        _active = true;
        actions.Add(BehaviorAction.ModifierDown(modifier));
    }

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions) => Release(actions);
    internal override void Cancel(List<BehaviorAction> actions) => Release(actions);

    private void Release(List<BehaviorAction> actions)
    {
        if (!_active)
            return;

        _active = false;
        actions.Add(BehaviorAction.ModifierUp(modifier));
    }
}

internal sealed class OneShotModifierBehaviorDefinition : BehaviorDefinition
{
    private readonly string _modifier;

    public OneShotModifierBehaviorDefinition(string modifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modifier);
        _modifier = modifier.Trim();
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new OneShotModifierBehaviorInstance(sourceKey, _modifier);
}

internal sealed class OneShotModifierBehaviorInstance(KeyId sourceKey, string modifier) : BehaviorInstance(sourceKey)
{
    private bool _active;
    private bool _interrupted;

    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
    {
        _active = true;
        actions.Add(BehaviorAction.ModifierDown(modifier));
    }

    internal override void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
        => _interrupted = true;

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
        if (!_active)
            return;

        _active = false;
        actions.Add(BehaviorAction.ModifierUp(modifier));
        if (!_interrupted)
            actions.Add(BehaviorAction.ModifierOneShot(modifier));
    }

    internal override void Cancel(List<BehaviorAction> actions)
    {
        if (!_active)
            return;

        _active = false;
        actions.Add(BehaviorAction.ModifierUp(modifier));
    }
}

internal sealed class PressActionBehaviorDefinition(BehaviorAction action) : BehaviorDefinition
{
    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new PressActionBehaviorInstance(sourceKey, action);
}

internal sealed class PressActionBehaviorInstance(KeyId sourceKey, BehaviorAction action) : BehaviorInstance(sourceKey)
{
    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
        => actions.Add(action);

    internal override void OnRepeat(long timestampMs, List<BehaviorAction> actions)
    {
        if (action.RepeatPolicy == BehaviorRepeatPolicy.PhysicalKeyDown)
            actions.Add(action);
    }

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
    }
}
