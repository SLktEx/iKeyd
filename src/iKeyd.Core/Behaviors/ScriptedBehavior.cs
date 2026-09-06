using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

namespace iKeyd.Core.Behaviors;

internal sealed class ScriptedBehaviorDefinition : BehaviorDefinition
{
    private readonly UserBehaviorDefinitionProfile _definition;
    private readonly IReadOnlyDictionary<string, string> _arguments;

    public ScriptedBehaviorDefinition(
        UserBehaviorDefinitionProfile definition,
        BehaviorInvocationProfile invocation)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.Arguments.Count != definition.Parameters.Count)
        {
            throw new InvalidDataException(
                $"Behavior '{definition.Name}' requires {definition.Parameters.Count} arguments but got {invocation.Arguments.Count}.");
        }
        if (invocation.Options.Count != 0)
            throw new InvalidDataException($"User behavior '{definition.Name}' does not support invocation options yet.");

        _definition = definition;
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < definition.Parameters.Count; index++)
            arguments.Add(definition.Parameters[index], invocation.Arguments[index]);
        _arguments = arguments;

        ValidateDefinition();
    }

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new ScriptedBehaviorInstance(sourceKey, _definition, _arguments);

    private void ValidateDefinition()
    {
        foreach (var handler in _definition.Handlers)
        {
            var eventName = handler.Event.ToLowerInvariant();
            switch (eventName)
            {
                case "press":
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
                    if (string.IsNullOrWhiteSpace(statement.Target) ||
                        !_definition.Locals.Any(local => string.Equals(local.Name, statement.Target, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidDataException($"set_bool targets unknown local '{statement.Target}'.");
                    }
                    if (!bool.TryParse(statement.Value, out _))
                        throw new InvalidDataException("set_bool value must be true or false.");
                    break;

                case "send":
                case "layer_on":
                case "layer_off":
                case "modifier_down":
                case "modifier_up":
                    ValidateOperand(statement.Value, statement.Op);
                    break;

                case "if_bool":
                    if (string.IsNullOrWhiteSpace(statement.Condition) ||
                        !_definition.Locals.Any(local => string.Equals(local.Name, statement.Condition, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidDataException($"if_bool references unknown local '{statement.Condition}'.");
                    }
                    ValidateStatements(statement.Then);
                    ValidateStatements(statement.Else);
                    break;

                default:
                    throw new InvalidDataException($"Unsupported user behavior operation '{statement.Op}'.");
            }
        }
    }

    private void ValidateOperand(string? operand, string op)
    {
        if (string.IsNullOrWhiteSpace(operand))
            throw new InvalidDataException($"{op} requires an operand.");
    }
}

internal sealed class ScriptedBehaviorInstance : BehaviorInstance
{
    private readonly UserBehaviorDefinitionProfile _definition;
    private readonly IReadOnlyDictionary<string, string> _arguments;
    private readonly Dictionary<string, bool> _locals;
    private readonly Dictionary<string, int> _ownedLayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _ownedModifiers = new(StringComparer.OrdinalIgnoreCase);
    private bool _released;

    public ScriptedBehaviorInstance(
        KeyId sourceKey,
        UserBehaviorDefinitionProfile definition,
        IReadOnlyDictionary<string, string> arguments)
        : base(sourceKey)
    {
        _definition = definition;
        _arguments = arguments;
        _locals = definition.Locals.ToDictionary(
            local => local.Name,
            local => local.InitialValue,
            StringComparer.OrdinalIgnoreCase);
    }

    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
        => ExecuteHandler("press", null, actions);

    internal override void OnInterrupt(KeyId otherKey, long timestampMs, List<BehaviorAction> actions)
        => ExecuteHandler("interrupt", otherKey.Value, actions);

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
        if (_released)
            return;
        ExecuteHandler("release", null, actions);
        CleanupOwned(actions);
        _released = true;
    }

    internal override void Cancel(List<BehaviorAction> actions)
    {
        if (_released)
            return;
        CleanupOwned(actions);
        _released = true;
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
                    _locals[statement.Target!] = bool.Parse(statement.Value!);
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
                        _locals[statement.Condition!] ? statement.Then : statement.Else,
                        eventArguments,
                        actions);
                    break;

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
