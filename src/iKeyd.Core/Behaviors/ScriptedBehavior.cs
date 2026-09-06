using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.State;

namespace iKeyd.Core.Behaviors;

internal sealed class ScriptedBehaviorDefinition : BehaviorDefinition
{
    private static readonly string[] TapHoldOptionNames =
    [
        "tapping_term",
        "hold_on_other_key_press"
    ];

    private readonly UserBehaviorDefinitionProfile _definition;
    private readonly IReadOnlyDictionary<string, string> _arguments;
    private readonly RuntimeStateProfile _stateProfile;
    private readonly IRuntimeStateStore _runtimeState;
    private readonly ScriptedTapHoldOptions _tapHoldOptions;

    public ScriptedBehaviorDefinition(
        UserBehaviorDefinitionProfile definition,
        BehaviorInvocationProfile invocation)
        : this(definition, invocation, RuntimeStateProfile.Empty, EmptyRuntimeStateStore.Instance)
    {
    }

    public ScriptedBehaviorDefinition(
        UserBehaviorDefinitionProfile definition,
        BehaviorInvocationProfile invocation,
        RuntimeStateProfile stateProfile,
        IRuntimeStateStore runtimeState)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(stateProfile);
        ArgumentNullException.ThrowIfNull(runtimeState);
        if (invocation.Arguments.Count != definition.Parameters.Count)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Name}' requires {definition.Parameters.Count} arguments but got {invocation.Arguments.Count}.");
        }

        _definition = definition;
        _stateProfile = stateProfile;
        _runtimeState = runtimeState;
        _tapHoldOptions = ReadTapHoldOptions(invocation);

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < definition.Parameters.Count; index++)
            arguments.Add(definition.Parameters[index], invocation.Arguments[index]);
        _arguments = arguments;

        ValidateDefinition();
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new ScriptedBehaviorInstance(
            sourceKey,
            _definition,
            _arguments,
            _runtimeState,
            _tapHoldOptions,
            timestampMs);

    private ScriptedTapHoldOptions ReadTapHoldOptions(BehaviorInvocationProfile invocation)
    {
        var enabled = _definition.FindHandler("tap") is not null ||
                      _definition.FindHandler("hold") is not null;
        if (!enabled)
        {
            if (invocation.Options.Count != 0)
                throw new InvalidDataException($"User behavior '{_definition.Name}' does not support invocation options yet.");
            return ScriptedTapHoldOptions.Disabled;
        }

        foreach (var option in invocation.Options.Keys)
        {
            if (!TapHoldOptionNames.Contains(option, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"User behavior '{_definition.Name}' does not support option '{option}'.");
        }

        var tappingTermMs = LayerTapOptions.DefaultTappingTermMs;
        if (invocation.Options.TryGetValue("tapping_term", out var rawTerm))
        {
            if (!rawTerm.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(rawTerm.AsSpan(0, rawTerm.Length - 2), out tappingTermMs) ||
                tappingTermMs < 0)
            {
                throw new InvalidDataException(
                    $"{_definition.Name}.tapping_term must be a non-negative duration such as '170ms'.");
            }
        }

        var holdOnOtherKeyPress = true;
        if (invocation.Options.TryGetValue("hold_on_other_key_press", out var rawHold))
        {
            if (!bool.TryParse(rawHold, out holdOnOtherKeyPress))
            {
                throw new InvalidDataException(
                    $"{_definition.Name}.hold_on_other_key_press must be true or false.");
            }
        }

        return new ScriptedTapHoldOptions(true, tappingTermMs, holdOnOtherKeyPress);
    }

    private void ValidateDefinition()
    {
        foreach (var handler in _definition.Handlers)
        {
            var eventName = handler.Event.ToLowerInvariant();
            switch (eventName)
            {
                case "press":
                case "hold":
                case "tap":
                case "release":
                    if (handler.Parameters.Count != 0)
                        throw new InvalidDataException($"Handler '{handler.Event}' does not accept parameters.");
                    break;
                case "interrupt":
                    if (handler.Parameters.Count > 1)
                        throw new InvalidDataException("interrupt handler accepts at most one parameter.");
                    break;
                default:
                    throw new InvalidDataException($"Unsupported user behavior event '{handler.Event}'.");
            }

            ValidateStatements(handler.Statements);
        }
    }

    private void ValidateStatements(IReadOnlyList<UserBehaviorStatementProfile> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement.Op.ToLowerInvariant())
            {
                case "set_bool":
                    _ = RequireLocal(statement.Target, statement.Op, UserBehaviorLocalType.Bool);
                    if (!bool.TryParse(statement.Value, out _))
                        throw new InvalidDataException("set_bool value must be true or false.");
                    break;

                case "set_int":
                case "add_int":
                    _ = RequireLocal(statement.Target, statement.Op, UserBehaviorLocalType.Int);
                    if (!int.TryParse(statement.Value, out _))
                        throw new InvalidDataException($"{statement.Op} value must be a 32-bit integer.");
                    break;

                case "state_set":
                {
                    var field = RequireStateField(statement.Target, statement.Op);
                    if (statement.Value is null)
                        throw new InvalidDataException("state_set requires a value.");
                    try
                    {
                        _ = field.NormalizeScalar(statement.Value);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException(exception.Message, exception);
                    }
                    break;
                }

                case "state_toggle":
                {
                    var field = RequireStateField(statement.Target, statement.Op);
                    if (field.Type != RuntimeStateType.Bool)
                        throw new InvalidDataException($"Runtime state field '{field.Name}' is not bool and cannot be toggled.");
                    break;
                }

                case "send":
                case "layer_on":
                case "layer_off":
                case "modifier_down":
                case "modifier_up":
                    ValidateOperand(statement.Value, statement.Op);
                    break;

                case "if_bool":
                    _ = RequireLocal(statement.Condition, statement.Op, UserBehaviorLocalType.Bool);
                    ValidateStatements(statement.Then);
                    ValidateStatements(statement.Else);
                    break;

                case "if_int_equals":
                case "if_int_not_equals":
                    _ = RequireLocal(statement.Target, statement.Op, UserBehaviorLocalType.Int);
                    if (!int.TryParse(statement.Value, out _))
                        throw new InvalidDataException($"{statement.Op} expected value must be a 32-bit integer.");
                    ValidateStatements(statement.Then);
                    ValidateStatements(statement.Else);
                    break;

                case "if_state_equals":
                case "if_state_not_equals":
                {
                    var field = RequireStateField(statement.Target, statement.Op);
                    if (statement.Value is null)
                        throw new InvalidDataException($"{statement.Op} requires an expected value.");
                    try
                    {
                        _ = field.NormalizeScalar(statement.Value);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException(exception.Message, exception);
                    }
                    ValidateStatements(statement.Then);
                    ValidateStatements(statement.Else);
                    break;
                }

                default:
                    throw new InvalidDataException($"Unsupported user behavior operation '{statement.Op}'.");
            }
        }
    }

    private UserBehaviorLocalProfile RequireLocal(
        string? name,
        string op,
        UserBehaviorLocalType expectedType)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"{op} requires a local name.");

        var local = _definition.Locals.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (local is null)
            throw new InvalidDataException($"{op} references unknown local '{name}'.");
        if (local.Type != expectedType)
        {
            throw new InvalidDataException(
                $"{op} requires a {expectedType.ToString().ToLowerInvariant()} local but '{local.Name}' is {local.Type.ToString().ToLowerInvariant()}.");
        }
        return local;
    }

    private RuntimeStateFieldProfile RequireStateField(string? name, string op)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"{op} requires a state field.");
        try
        {
            return _stateProfile.GetField(name);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    private static void ValidateOperand(string? operand, string op)
    {
        if (string.IsNullOrWhiteSpace(operand))
            throw new InvalidDataException($"{op} requires an operand.");
    }
}

internal readonly record struct ScriptedTapHoldOptions(
    bool Enabled,
    int TappingTermMs,
    bool HoldOnOtherKeyPress)
{
    public static ScriptedTapHoldOptions Disabled { get; } = new(false, 0, false);
}

internal sealed class ScriptedBehaviorInstance : BehaviorInstance
{
    private enum TapHoldResolution
    {
        Disabled,
        Pending,
        Hold,
        Released
    }

    private readonly UserBehaviorDefinitionProfile _definition;
    private readonly IReadOnlyDictionary<string, string> _arguments;
    private readonly IRuntimeStateStore _runtimeState;
    private readonly Dictionary<string, bool> _boolLocals;
    private readonly Dictionary<string, int> _intLocals;
    private readonly Dictionary<string, int> _ownedLayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _ownedModifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScriptedTapHoldOptions _tapHoldOptions;
    private readonly long _pressedAtMs;
    private TapHoldResolution _tapHoldResolution;
    private bool _released;

    public ScriptedBehaviorInstance(
        KeyId sourceKey,
        UserBehaviorDefinitionProfile definition,
        IReadOnlyDictionary<string, string> arguments,
        IRuntimeStateStore runtimeState,
        ScriptedTapHoldOptions tapHoldOptions,
        long pressedAtMs)
        : base(sourceKey)
    {
        _definition = definition;
        _arguments = arguments;
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _tapHoldOptions = tapHoldOptions;
        _pressedAtMs = pressedAtMs;
        _tapHoldResolution = tapHoldOptions.Enabled
            ? TapHoldResolution.Pending
            : TapHoldResolution.Disabled;
        _boolLocals = definition.Locals
            .Where(local => local.Type == UserBehaviorLocalType.Bool)
            .ToDictionary(
                local => local.Name,
                local => local.InitialBoolValue,
                StringComparer.OrdinalIgnoreCase);
        _intLocals = definition.Locals
            .Where(local => local.Type == UserBehaviorLocalType.Int)
            .ToDictionary(
                local => local.Name,
                local => local.InitialIntValue,
                StringComparer.OrdinalIgnoreCase);
    }

    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
        => ExecuteHandler("press", null, actions);

    internal override void AdvanceTo(long timestampMs, List<BehaviorAction> actions)
    {
        if (_released || _tapHoldResolution != TapHoldResolution.Pending)
            return;

        if (timestampMs - _pressedAtMs >= _tapHoldOptions.TappingTermMs)
            ResolveHold(actions);
    }

    internal override void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
    {
        if (_tapHoldResolution == TapHoldResolution.Pending && _tapHoldOptions.HoldOnOtherKeyPress)
            ResolveHold(actions);

        ExecuteHandler("interrupt", otherKey.Value, actions);
    }

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
        if (_released)
            return;

        if (_tapHoldResolution == TapHoldResolution.Pending)
            ExecuteHandler("tap", null, actions);

        ExecuteHandler("release", null, actions);
        CleanupOwned(actions);
        _tapHoldResolution = TapHoldResolution.Released;
        _released = true;
    }

    internal override void Cancel(List<BehaviorAction> actions)
    {
        if (_released)
            return;
        CleanupOwned(actions);
        _tapHoldResolution = TapHoldResolution.Released;
        _released = true;
    }

    private void ResolveHold(List<BehaviorAction> actions)
    {
        if (_tapHoldResolution != TapHoldResolution.Pending)
            return;

        _tapHoldResolution = TapHoldResolution.Hold;
        ExecuteHandler("hold", null, actions);
    }

    private void ExecuteHandler(string eventName, string? eventValue, List<BehaviorAction> actions)
    {
        var handler = _definition.FindHandler(eventName);
        if (handler is null)
            return;

        Dictionary<string, string>? eventArguments = null;
        if (handler.Parameters.Count == 1 && eventValue is not null)
        {
            eventArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [handler.Parameters[0]] = eventValue
            };
        }

        ExecuteStatements(handler.Statements, eventArguments, actions);
    }

    private void ExecuteStatements(
        IReadOnlyList<UserBehaviorStatementProfile> statements,
        IReadOnlyDictionary<string, string>? eventArguments,
        List<BehaviorAction> actions)
    {
        foreach (var statement in statements)
        {
            switch (statement.Op.ToLowerInvariant())
            {
                case "set_bool":
                    _boolLocals[statement.Target!] = bool.Parse(statement.Value!);
                    break;

                case "set_int":
                    _intLocals[statement.Target!] = int.Parse(statement.Value!, System.Globalization.CultureInfo.InvariantCulture);
                    break;

                case "add_int":
                    _intLocals[statement.Target!] = checked(
                        _intLocals[statement.Target!] +
                        int.Parse(statement.Value!, System.Globalization.CultureInfo.InvariantCulture));
                    break;

                case "state_set":
                    _runtimeState.SetScalar(statement.Target!, statement.Value!);
                    break;

                case "state_toggle":
                    _runtimeState.Toggle(statement.Target!);
                    break;

                case "send":
                    actions.Add(BehaviorAction.SendKey(new KeyId(ResolveOperand(statement.Value!, eventArguments))));
                    break;

                case "layer_on":
                {
                    var layer = ResolveOperand(statement.Value!, eventArguments);
                    actions.Add(BehaviorAction.LayerOn(layer));
                    AddOwned(_ownedLayers, layer);
                    break;
                }

                case "layer_off":
                {
                    var layer = ResolveOperand(statement.Value!, eventArguments);
                    actions.Add(BehaviorAction.LayerOff(layer));
                    RemoveOwned(_ownedLayers, layer);
                    break;
                }

                case "modifier_down":
                {
                    var modifier = ResolveOperand(statement.Value!, eventArguments);
                    actions.Add(BehaviorAction.ModifierDown(modifier));
                    AddOwned(_ownedModifiers, modifier);
                    break;
                }

                case "modifier_up":
                {
                    var modifier = ResolveOperand(statement.Value!, eventArguments);
                    actions.Add(BehaviorAction.ModifierUp(modifier));
                    RemoveOwned(_ownedModifiers, modifier);
                    break;
                }

                case "if_bool":
                    ExecuteStatements(
                        _boolLocals[statement.Condition!] ? statement.Then : statement.Else,
                        eventArguments,
                        actions);
                    break;

                case "if_int_equals":
                case "if_int_not_equals":
                {
                    var expected = int.Parse(statement.Value!, System.Globalization.CultureInfo.InvariantCulture);
                    var equals = _intLocals[statement.Target!] == expected;
                    var matches = statement.Op.Equals("if_int_equals", StringComparison.OrdinalIgnoreCase)
                        ? equals
                        : !equals;
                    ExecuteStatements(matches ? statement.Then : statement.Else, eventArguments, actions);
                    break;
                }

                case "if_state_equals":
                case "if_state_not_equals":
                {
                    var found = _runtimeState.TryGetScalar(statement.Target!, out var actual);
                    var equals = found && string.Equals(actual, statement.Value, StringComparison.OrdinalIgnoreCase);
                    var matches = statement.Op.Equals("if_state_equals", StringComparison.OrdinalIgnoreCase)
                        ? equals
                        : found && !equals;
                    ExecuteStatements(matches ? statement.Then : statement.Else, eventArguments, actions);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unsupported user behavior operation '{statement.Op}'.");
            }
        }
    }

    private string ResolveOperand(
        string operand,
        IReadOnlyDictionary<string, string>? eventArguments)
    {
        if (_arguments.TryGetValue(operand, out var argument))
            return argument;
        if (eventArguments is not null && eventArguments.TryGetValue(operand, out var eventArgument))
            return eventArgument;
        return operand;
    }

    private void CleanupOwned(List<BehaviorAction> actions)
    {
        foreach (var pair in _ownedLayers)
        {
            for (var index = 0; index < pair.Value; index++)
                actions.Add(BehaviorAction.LayerOff(pair.Key));
        }
        foreach (var pair in _ownedModifiers)
        {
            for (var index = 0; index < pair.Value; index++)
                actions.Add(BehaviorAction.ModifierUp(pair.Key));
        }
        _ownedLayers.Clear();
        _ownedModifiers.Clear();
    }

    private static void AddOwned(Dictionary<string, int> owned, string name)
    {
        owned.TryGetValue(name, out var count);
        owned[name] = count + 1;
    }

    private static void RemoveOwned(Dictionary<string, int> owned, string name)
    {
        if (!owned.TryGetValue(name, out var count))
            return;
        if (count <= 1)
            owned.Remove(name);
        else
            owned[name] = count - 1;
    }
}
