using iKeyd.Core.Automation;
using iKeyd.Core.Chords;

namespace iKeyd.Core.Configuration;

public enum KeyBehaviorActionKind
{
    Key,
    Text,
    Layer,
    Modifier,
    MouseMove,
    MouseClick,
    Scroll,
    Media,
    Window,
    Clipboard,
    Macro,
    Exec,
    Shell,
    Query,
    When
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

public enum SystemQueryConditionOperator
{
    Equals,
    NotEquals
}

public sealed record SystemQueryCondition
{
    public SystemQueryCondition(string query, SystemQueryConditionOperator @operator, string expected)
    {
        Query = SystemQueryKeys.Normalize(query);
        Operator = @operator;
        Expected = expected ?? throw new ArgumentNullException(nameof(expected));
    }

    public string Query { get; }
    public SystemQueryConditionOperator Operator { get; }
    public string Expected { get; }

    public bool Evaluate(ISystemQuerySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.TryGetValue(Query, out var actual))
            return false;
        var equals = string.Equals(actual, Expected, StringComparison.OrdinalIgnoreCase);
        return Operator == SystemQueryConditionOperator.Equals ? equals : !equals;
    }
}

public sealed record ConditionalBehaviorAction(
    SystemQueryCondition Condition,
    KeyBehaviorAction Then,
    KeyBehaviorAction? Else);

public readonly record struct KeyBehaviorAction(
    KeyBehaviorActionKind Kind,
    string Value,
    IReadOnlyList<string>? Arguments = null,
    ConditionalBehaviorAction? Conditional = null)
{
    public static KeyBehaviorAction Key(string key) => new(KeyBehaviorActionKind.Key, RequireValue(key, nameof(key)));
    public static KeyBehaviorAction Text(string text) => new(KeyBehaviorActionKind.Text, text ?? throw new ArgumentNullException(nameof(text)));
    public static KeyBehaviorAction Layer(string layer) => new(KeyBehaviorActionKind.Layer, RequireValue(layer, nameof(layer)));
    public static KeyBehaviorAction Modifier(KeyBehaviorModifier modifier) => new(KeyBehaviorActionKind.Modifier, modifier.ToString());
    public static KeyBehaviorAction MouseMove(int deltaX, int deltaY) => new(KeyBehaviorActionKind.MouseMove, $"{deltaX},{deltaY}");
    public static KeyBehaviorAction MouseClick(string button) => new(KeyBehaviorActionKind.MouseClick, RequireValue(button, nameof(button)));
    public static KeyBehaviorAction Scroll(string direction) => new(KeyBehaviorActionKind.Scroll, RequireValue(direction, nameof(direction)));
    public static KeyBehaviorAction Media(string command) => new(KeyBehaviorActionKind.Media, RequireValue(command, nameof(command)));
    public static KeyBehaviorAction Window(string command) => new(KeyBehaviorActionKind.Window, RequireValue(command, nameof(command)));
    public static KeyBehaviorAction Clipboard(string command) => new(KeyBehaviorActionKind.Clipboard, RequireValue(command, nameof(command)));
    public static KeyBehaviorAction Macro(string template) => new(KeyBehaviorActionKind.Macro, template ?? throw new ArgumentNullException(nameof(template)));
    public static KeyBehaviorAction Exec(string executable, IEnumerable<string>? arguments = null)
    {
        var argv = arguments?.Select(argument => argument ?? throw new ArgumentException("Exec arguments must not contain null.", nameof(arguments))).ToArray() ?? [];
        return new KeyBehaviorAction(KeyBehaviorActionKind.Exec, RequireValue(executable, nameof(executable)), Array.AsReadOnly(argv));
    }
    public static KeyBehaviorAction Shell(string command) => new(KeyBehaviorActionKind.Shell, RequireValue(command, nameof(command)));
    public static KeyBehaviorAction Query(string key) => new(KeyBehaviorActionKind.Query, SystemQueryKeys.Normalize(key));
    public static KeyBehaviorAction When(SystemQueryCondition condition, KeyBehaviorAction thenAction, KeyBehaviorAction? elseAction = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (thenAction.IsHoldAction || elseAction is { IsHoldAction: true })
            throw new ArgumentException("Conditional branches must be output actions, not layer/modifier holds.");
        return new KeyBehaviorAction(
            KeyBehaviorActionKind.When,
            string.Empty,
            null,
            new ConditionalBehaviorAction(condition, thenAction, elseAction));
    }

    public IReadOnlyList<string> GetArguments() => Arguments ?? Array.Empty<string>();

    public ConditionalBehaviorAction GetConditional()
        => Kind == KeyBehaviorActionKind.When && Conditional is not null
            ? Conditional
            : throw new InvalidOperationException("Behavior action is not a conditional action.");

    public KeyBehaviorModifier GetModifier()
        => Kind == KeyBehaviorActionKind.Modifier && Enum.TryParse<KeyBehaviorModifier>(Value, ignoreCase: true, out var modifier)
            ? modifier
            : throw new InvalidOperationException($"Behavior action '{Kind}:{Value}' is not a modifier.");

    public bool IsHoldAction => Kind is KeyBehaviorActionKind.Layer or KeyBehaviorActionKind.Modifier;

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
        if (tap is { IsHoldAction: true })
            throw new ArgumentException("Tap actions cannot be layer or modifier actions.", nameof(tap));
        if (!hold.IsHoldAction)
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
        if (action.IsHoldAction)
            throw new ArgumentException("Layer mappings must emit output actions, not layer/modifier holds.", nameof(action));
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
    private readonly IReadOnlyList<string> _systemQueries;

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

        var queries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var behavior in byTrigger.Values)
            if (behavior.Tap is { } tap)
                CollectSystemQueries(tap, queries);
        foreach (var layer in byName.Values)
            foreach (var action in layer.Bindings.Values)
                CollectSystemQueries(action, queries);

        _behaviors = byTrigger;
        _layers = byName;
        _systemQueries = queries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static KeyBehaviorProfile Empty { get; } = new();

    public IReadOnlyDictionary<KeyId, KeyBehaviorBinding> Behaviors => _behaviors;
    public IReadOnlyDictionary<string, KeyBehaviorLayer> Layers => _layers;
    public IReadOnlyList<string> SystemQueries => _systemQueries;
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

    private static void CollectSystemQueries(KeyBehaviorAction action, ISet<string> queries)
    {
        if (action.Kind == KeyBehaviorActionKind.Query)
            queries.Add(SystemQueryKeys.Normalize(action.Value));
        if (action.Kind != KeyBehaviorActionKind.When)
            return;

        var conditional = action.GetConditional();
        queries.Add(conditional.Condition.Query);
        CollectSystemQueries(conditional.Then, queries);
        if (conditional.Else is { } elseAction)
            CollectSystemQueries(elseAction, queries);
    }
}
