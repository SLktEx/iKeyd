using iKeyd.Core.Chords;

namespace iKeyd.Core.Configuration;

public enum KeyBehaviorActionKind
{
    Key,
    Text,
    Layer,
    Modifier
}

public enum KeyBehaviorModifier
{
    Control,
    Shift,
    Alt,
    Gui
}

public enum TapHoldInterruptPolicy
{
    Hold,
    Tap
}

public readonly record struct KeyBehaviorAction(KeyBehaviorActionKind Kind, string Value)
{
    public static KeyBehaviorAction Key(string key) => new(KeyBehaviorActionKind.Key, RequireValue(key, nameof(key)));
    public static KeyBehaviorAction Text(string text) => new(KeyBehaviorActionKind.Text, text ?? throw new ArgumentNullException(nameof(text)));
    public static KeyBehaviorAction Layer(string layer) => new(KeyBehaviorActionKind.Layer, RequireValue(layer, nameof(layer)));
    public static KeyBehaviorAction Modifier(KeyBehaviorModifier modifier) => new(KeyBehaviorActionKind.Modifier, modifier.ToString());

    public KeyBehaviorModifier GetModifier()
        => Kind == KeyBehaviorActionKind.Modifier && Enum.TryParse<KeyBehaviorModifier>(Value, ignoreCase: true, out var modifier)
            ? modifier
            : throw new InvalidOperationException($"Behavior action '{Kind}:{Value}' is not a modifier.");

    private static string RequireValue(string value, string parameterName)
        => !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("Behavior action value must not be empty.", parameterName);
}

public sealed record KeyBehaviorBinding
{
    public KeyBehaviorBinding(
        KeyId trigger,
        KeyBehaviorAction? tap,
        KeyBehaviorAction hold,
        int timeoutMs = 180,
        TapHoldInterruptPolicy interrupt = TapHoldInterruptPolicy.Hold)
    {
        if (timeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Tap/hold timeout must be positive.");
        if (tap is { Kind: KeyBehaviorActionKind.Layer or KeyBehaviorActionKind.Modifier })
            throw new ArgumentException("Tap actions must be key or text actions.", nameof(tap));
        if (hold.Kind is not (KeyBehaviorActionKind.Layer or KeyBehaviorActionKind.Modifier))
            throw new ArgumentException("Hold actions must be layer or modifier actions.", nameof(hold));

        Trigger = trigger;
        Tap = tap;
        Hold = hold;
        TimeoutMs = timeoutMs;
        Interrupt = interrupt;
    }

    public KeyId Trigger { get; }
    public KeyBehaviorAction? Tap { get; }
    public KeyBehaviorAction Hold { get; }
    public int TimeoutMs { get; }
    public TapHoldInterruptPolicy Interrupt { get; }
}

public sealed record KeyBehaviorLayerBinding
{
    public KeyBehaviorLayerBinding(KeyId key, KeyBehaviorAction action)
    {
        if (action.Kind is not (KeyBehaviorActionKind.Key or KeyBehaviorActionKind.Text))
            throw new ArgumentException("Layer mappings must currently emit key or text actions.", nameof(action));
        Key = key;
        Action = action;
    }

    public KeyId Key { get; }
    public KeyBehaviorAction Action { get; }
}

public sealed class KeyBehaviorLayer
{
    private readonly IReadOnlyDictionary<KeyId, KeyBehaviorAction> _bindings;

    public KeyBehaviorLayer(string name, IEnumerable<KeyBehaviorLayerBinding> bindings)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Layer name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(bindings);

        Name = name.Trim();
        var byKey = new Dictionary<KeyId, KeyBehaviorAction>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!byKey.TryAdd(binding.Key, binding.Action))
                throw new ArgumentException($"Duplicate key '{binding.Key}' in behavior layer '{Name}'.", nameof(bindings));
        }
        _bindings = byKey;
    }

    public string Name { get; }
    public IReadOnlyDictionary<KeyId, KeyBehaviorAction> Bindings => _bindings;
    public bool TryGetAction(KeyId key, out KeyBehaviorAction action) => _bindings.TryGetValue(key, out action);
}

public sealed class KeyBehaviorProfile
{
    private readonly IReadOnlyDictionary<KeyId, KeyBehaviorBinding> _behaviors;
    private readonly IReadOnlyDictionary<string, KeyBehaviorLayer> _layers;

    public KeyBehaviorProfile(
        IEnumerable<KeyBehaviorBinding>? behaviors = null,
        IEnumerable<KeyBehaviorLayer>? layers = null)
    {
        var byTrigger = new Dictionary<KeyId, KeyBehaviorBinding>();
        foreach (var behavior in behaviors ?? [])
        {
            ArgumentNullException.ThrowIfNull(behavior);
            if (!byTrigger.TryAdd(behavior.Trigger, behavior))
                throw new ArgumentException($"Duplicate behavior trigger '{behavior.Trigger}'.", nameof(behaviors));
        }

        var byName = new Dictionary<string, KeyBehaviorLayer>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers ?? [])
        {
            ArgumentNullException.ThrowIfNull(layer);
            if (!byName.TryAdd(layer.Name, layer))
                throw new ArgumentException($"Duplicate behavior layer '{layer.Name}'.", nameof(layers));
        }

        foreach (var behavior in byTrigger.Values)
        {
            if (behavior.Hold.Kind == KeyBehaviorActionKind.Layer && !byName.ContainsKey(behavior.Hold.Value))
                throw new ArgumentException($"Behavior '{behavior.Trigger}' references unknown layer '{behavior.Hold.Value}'.", nameof(behaviors));
        }

        _behaviors = byTrigger;
        _layers = byName;
    }

    public static KeyBehaviorProfile Empty { get; } = new();

    public IReadOnlyDictionary<KeyId, KeyBehaviorBinding> Behaviors => _behaviors;
    public IReadOnlyDictionary<string, KeyBehaviorLayer> Layers => _layers;
    public bool IsEmpty => _behaviors.Count == 0 && _layers.Count == 0;

    public bool TryGetBehavior(KeyId trigger, out KeyBehaviorBinding behavior)
        => _behaviors.TryGetValue(trigger, out behavior!);

    public bool TryGetLayerAction(string layer, KeyId key, out KeyBehaviorAction action)
    {
        if (_layers.TryGetValue(layer, out var configuredLayer))
            return configuredLayer.TryGetAction(key, out action);
        action = default;
        return false;
    }
}
