using iKeyd.Core.Automation;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.State;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// Converts the profile/IR representation of standard and user-defined behavior
/// invocations into executable behavior definitions. The event runtime remains
/// generic and does not branch on behavior names.
/// </summary>
public static class BehaviorDefinitionFactory
{
    private static readonly string[] TapHoldOptionNames =
    [
        "tapping_term",
        "hold_on_other_key_press"
    ];

    public static BehaviorDefinition Create(BehaviorInvocationProfile invocation)
        => Create(
            invocation,
            new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase),
            EmptySystemQuerySnapshot.Instance,
            RuntimeStateProfile.Empty,
            EmptyRuntimeStateStore.Instance);

    public static BehaviorDefinition Create(
        BehaviorInvocationProfile invocation,
        IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> userDefinitions)
        => Create(
            invocation,
            userDefinitions,
            EmptySystemQuerySnapshot.Instance,
            RuntimeStateProfile.Empty,
            EmptyRuntimeStateStore.Instance);

    public static BehaviorDefinition Create(
        BehaviorInvocationProfile invocation,
        IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> userDefinitions,
        ISystemQuerySnapshot systemQueries)
        => Create(
            invocation,
            userDefinitions,
            systemQueries,
            RuntimeStateProfile.Empty,
            EmptyRuntimeStateStore.Instance);

    public static BehaviorDefinition Create(
        BehaviorInvocationProfile invocation,
        IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> userDefinitions,
        ISystemQuerySnapshot systemQueries,
        RuntimeStateProfile stateProfile,
        IRuntimeStateStore runtimeState)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(userDefinitions);
        ArgumentNullException.ThrowIfNull(systemQueries);
        ArgumentNullException.ThrowIfNull(stateProfile);
        ArgumentNullException.ThrowIfNull(runtimeState);

        if (string.Equals(invocation.Name, "LT", StringComparison.OrdinalIgnoreCase))
            return CreateLayerTap(invocation);
        if (string.Equals(invocation.Name, "MT", StringComparison.OrdinalIgnoreCase))
            return CreateModTap(invocation);
        if (string.Equals(invocation.Name, "MO", StringComparison.OrdinalIgnoreCase))
            return CreateMomentaryLayer(invocation);
        if (string.Equals(invocation.Name, "MOD", StringComparison.OrdinalIgnoreCase))
            return CreateModifierHold(invocation);
        if (string.Equals(invocation.Name, "TG", StringComparison.OrdinalIgnoreCase))
            return CreateToggleLayer(invocation);
        if (string.Equals(invocation.Name, "TO", StringComparison.OrdinalIgnoreCase))
            return CreateSetLayer(invocation);
        if (string.Equals(invocation.Name, "OSL", StringComparison.OrdinalIgnoreCase))
            return CreateOneShotLayer(invocation);
        if (string.Equals(invocation.Name, "OSM", StringComparison.OrdinalIgnoreCase))
            return CreateOneShotModifier(invocation);
        if (string.Equals(invocation.Name, "UNICODE", StringComparison.OrdinalIgnoreCase))
            return CreateUnicode(invocation);
        if (string.Equals(invocation.Name, "TEXT", StringComparison.OrdinalIgnoreCase))
            return CreateText(invocation);
        if (string.Equals(invocation.Name, "EXEC", StringComparison.OrdinalIgnoreCase))
            return CreateExec(invocation);
        if (string.Equals(invocation.Name, "SHELL", StringComparison.OrdinalIgnoreCase))
            return CreateShell(invocation);
        if (string.Equals(invocation.Name, "QUERY", StringComparison.OrdinalIgnoreCase))
            return CreateQuery(invocation);
        if (string.Equals(invocation.Name, "SET", StringComparison.OrdinalIgnoreCase))
            return CreateStateSet(invocation, stateProfile);
        if (string.Equals(invocation.Name, "TOGGLE", StringComparison.OrdinalIgnoreCase))
            return CreateStateToggle(invocation, stateProfile);
        if (string.Equals(invocation.Name, "WHEN", StringComparison.OrdinalIgnoreCase))
            return CreateWhen(invocation, systemQueries, stateProfile, runtimeState);
        if (userDefinitions.TryGetValue(invocation.Name, out var userDefinition))
            return new ScriptedBehaviorDefinition(userDefinition, invocation, stateProfile, runtimeState);

        throw new NotSupportedException($"Unknown behavior '{invocation.Name}'.");
    }

    public static IReadOnlyList<string> GetRequiredSystemQueries(BehaviorInvocationProfile invocation)
        => GetRequiredSystemQueries(invocation, RuntimeStateProfile.Empty);

    public static IReadOnlyList<string> GetRequiredSystemQueries(
        BehaviorInvocationProfile invocation,
        RuntimeStateProfile stateProfile)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(stateProfile);
        var queries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(invocation.Name, "QUERY", StringComparison.OrdinalIgnoreCase))
        {
            queries.Add(ReadQueryKey(invocation));
        }
        else if (string.Equals(invocation.Name, "WHEN", StringComparison.OrdinalIgnoreCase))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var branch = ReadConditionalBranch(invocation, string.Empty, used, stateProfile);
            ValidateNoUnusedWhenOptions(invocation, used);
            branch.CollectSystemQueries(queries);
        }
        else if (string.Equals(invocation.Name, "SET", StringComparison.OrdinalIgnoreCase))
        {
            _ = ReadStateSet(invocation, stateProfile);
        }
        else if (string.Equals(invocation.Name, "TOGGLE", StringComparison.OrdinalIgnoreCase))
        {
            _ = ReadToggleField(invocation, stateProfile);
        }

        return queries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static BehaviorDefinition CreateLayerTap(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count != 2)
            throw new InvalidDataException("LT requires exactly two arguments: LT(layer, tap_key).");

        ValidateKnownOptions(invocation, TapHoldOptionNames);
        var layer = invocation.Arguments[0];
        var tapKey = new KeyId(invocation.Arguments[1]);
        var options = new LayerTapOptions
        {
            TappingTermMs = ReadDurationMs(
                invocation,
                "tapping_term",
                LayerTapOptions.DefaultTappingTermMs),
            HoldOnOtherKeyPress = ReadBoolean(invocation, "hold_on_other_key_press", true)
        };
        return StandardBehaviors.LT(layer, tapKey, options);
    }

    private static BehaviorDefinition CreateModTap(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count != 2)
            throw new InvalidDataException("MT requires exactly two arguments: MT(modifier, tap_key).");

        ValidateKnownOptions(invocation, TapHoldOptionNames);
        var modifier = invocation.Arguments[0];
        var tapKey = new KeyId(invocation.Arguments[1]);
        var options = new ModTapOptions
        {
            TappingTermMs = ReadDurationMs(
                invocation,
                "tapping_term",
                ModTapOptions.DefaultTappingTermMs),
            HoldOnOtherKeyPress = ReadBoolean(invocation, "hold_on_other_key_press", true)
        };
        return StandardBehaviors.MT(modifier, tapKey, options);
    }

    private static BehaviorDefinition CreateMomentaryLayer(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "MO(layer)");
        return StandardBehaviors.MO(invocation.Arguments[0]);
    }

    private static BehaviorDefinition CreateModifierHold(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "MOD(modifier)");
        return StandardBehaviors.MOD(NormalizeModifier(invocation.Arguments[0]));
    }

    private static BehaviorDefinition CreateToggleLayer(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "TG(layer)");
        return StandardBehaviors.TG(invocation.Arguments[0]);
    }

    private static BehaviorDefinition CreateSetLayer(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "TO(layer)");
        return StandardBehaviors.TO(invocation.Arguments[0]);
    }

    private static BehaviorDefinition CreateOneShotLayer(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "OSL(layer)");
        return StandardBehaviors.OSL(invocation.Arguments[0]);
    }

    private static BehaviorDefinition CreateOneShotModifier(BehaviorInvocationProfile invocation)
    {
        RequireNoOptions(invocation);
        RequireCount(invocation, 1, "OSM(modifier)");
        return StandardBehaviors.OSM(NormalizeModifier(invocation.Arguments[0]));
    }

    private static BehaviorDefinition CreateUnicode(BehaviorInvocationProfile invocation)
        => StandardBehaviors.Unicode(ReadLiteralValue(invocation, "UNICODE"));

    private static BehaviorDefinition CreateText(BehaviorInvocationProfile invocation)
        => StandardBehaviors.Text(ReadLiteralValue(invocation, "TEXT"));

    private static BehaviorDefinition CreateExec(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count != 0)
        {
            RequireNoOptions(invocation);
            return StandardBehaviors.Press(
                BehaviorAction.Exec(invocation.Arguments[0], invocation.Arguments.Skip(1)));
        }

        ValidateExecOptions(invocation);
        var executable = RequireOption(invocation, "executable");
        var arguments = ReadIndexedOptions(invocation, "arg");
        return StandardBehaviors.Press(BehaviorAction.Exec(executable, arguments));
    }

    private static BehaviorDefinition CreateShell(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count == 1 && invocation.Options.Count == 0)
            return StandardBehaviors.Press(BehaviorAction.Shell(invocation.Arguments[0]));

        RequireCount(invocation, 0, "SHELL() { command = \"...\" }");
        ValidateKnownOptions(invocation, ["command"]);
        return StandardBehaviors.Press(BehaviorAction.Shell(RequireOption(invocation, "command")));
    }

    private static BehaviorDefinition CreateQuery(BehaviorInvocationProfile invocation)
    {
        var key = ReadQueryKey(invocation);
        try
        {
            return StandardBehaviors.Press(BehaviorAction.Query(key));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    private static string ReadQueryKey(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count == 1 && invocation.Options.Count == 0)
            return SystemQueryKeys.Normalize(invocation.Arguments[0]);

        RequireCount(invocation, 0, "QUERY() { key = foreground.process }");
        ValidateKnownOptions(invocation, ["key"]);
        return SystemQueryKeys.Normalize(RequireOption(invocation, "key"));
    }

    private static BehaviorDefinition CreateStateSet(
        BehaviorInvocationProfile invocation,
        RuntimeStateProfile stateProfile)
    {
        var (field, value) = ReadStateSet(invocation, stateProfile);
        return StandardBehaviors.Press(BehaviorAction.StateSet(field.Name, value));
    }

    private static (RuntimeStateFieldProfile Field, string Value) ReadStateSet(
        BehaviorInvocationProfile invocation,
        RuntimeStateProfile stateProfile)
    {
        string fieldName;
        string rawValue;
        if (invocation.Arguments.Count == 2 && invocation.Options.Count == 0)
        {
            fieldName = invocation.Arguments[0];
            rawValue = invocation.Arguments[1];
        }
        else
        {
            RequireCount(invocation, 0, "SET() { state = mode; value = \"coding\" }");
            ValidateKnownOptions(invocation, ["state", "value"]);
            fieldName = RequireOption(invocation, "state");
            rawValue = RequireOption(invocation, "value");
        }

        var field = stateProfile.GetField(fieldName);
        try
        {
            return (field, field.NormalizeScalar(rawValue));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    private static BehaviorDefinition CreateStateToggle(
        BehaviorInvocationProfile invocation,
        RuntimeStateProfile stateProfile)
    {
        var field = ReadToggleField(invocation, stateProfile);
        return StandardBehaviors.Press(BehaviorAction.StateToggle(field.Name));
    }

    private static RuntimeStateFieldProfile ReadToggleField(
        BehaviorInvocationProfile invocation,
        RuntimeStateProfile stateProfile)
    {
        string fieldName;
        if (invocation.Arguments.Count == 1 && invocation.Options.Count == 0)
        {
            fieldName = invocation.Arguments[0];
        }
        else
        {
            RequireCount(invocation, 0, "TOGGLE() { state = nav_locked }");
            ValidateKnownOptions(invocation, ["state"]);
            fieldName = RequireOption(invocation, "state");
        }

        var field = stateProfile.GetField(fieldName);
        if (field.Type != RuntimeStateType.Bool)
            throw new InvalidDataException($"Runtime state field '{field.Name}' is not bool and cannot be toggled.");
        return field;
    }

    private static BehaviorDefinition CreateWhen(
        BehaviorInvocationProfile invocation,
        ISystemQuerySnapshot systemQueries,
        RuntimeStateProfile stateProfile,
        IRuntimeStateSnapshot runtimeState)
    {
        RequireCount(invocation, 0, "WHEN() { query|state = ... }");
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var branch = ReadConditionalBranch(invocation, string.Empty, used, stateProfile);
        ValidateNoUnusedWhenOptions(invocation, used);
        return new ConditionalBehaviorDefinition(branch, systemQueries, runtimeState);
    }

    private static BehaviorOutputBranch ReadConditionalBranch(
        BehaviorInvocationProfile invocation,
        string prefix,
        ISet<string> used,
        RuntimeStateProfile stateProfile)
    {
        var @operator = ParseConditionOperator(ReadUsedOption(invocation, prefix + "operator", used));
        var expected = ReadUsedOption(invocation, prefix + "expected", used);
        var queryKey = prefix + "query";
        var stateKey = prefix + "state";
        var hasQuery = invocation.Options.TryGetValue(queryKey, out var query);
        var hasState = invocation.Options.TryGetValue(stateKey, out var state);
        if (hasQuery == hasState)
            throw new InvalidDataException($"WHEN requires exactly one of '{queryKey}' or '{stateKey}'.");

        IBehaviorCondition condition;
        if (hasQuery)
        {
            used.Add(queryKey);
            condition = new SystemQueryCondition(SystemQueryKeys.Normalize(query!), @operator, expected);
        }
        else
        {
            used.Add(stateKey);
            var field = stateProfile.GetField(state!);
            try
            {
                condition = new RuntimeStateCondition(field, @operator, expected);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }
        }

        var thenBranch = ReadOutputBranch(invocation, prefix + "then_", used, required: true, stateProfile)!;
        var elseBranch = ReadOutputBranch(invocation, prefix + "else_", used, required: false, stateProfile);
        return BehaviorOutputBranch.When(condition, thenBranch, elseBranch);
    }

    private static BehaviorOutputBranch? ReadOutputBranch(
        BehaviorInvocationProfile invocation,
        string prefix,
        ISet<string> used,
        bool required,
        RuntimeStateProfile stateProfile)
    {
        var kindKey = prefix + "kind";
        if (!invocation.Options.TryGetValue(kindKey, out var rawKind))
        {
            if (required)
                throw new InvalidDataException($"WHEN requires option '{kindKey}'.");
            return null;
        }
        used.Add(kindKey);

        var kind = rawKind.Trim().ToUpperInvariant();
        if (kind == "WHEN")
            return ReadConditionalBranch(invocation, prefix, used, stateProfile);

        BehaviorAction action;
        if (kind == "TOGGLE")
        {
            var field = ReadUsedStateField(invocation, prefix + "state", used, stateProfile);
            if (field.Type != RuntimeStateType.Bool)
                throw new InvalidDataException($"Runtime state field '{field.Name}' is not bool and cannot be toggled.");
            action = BehaviorAction.StateToggle(field.Name);
        }
        else if (kind == "SET")
        {
            var field = ReadUsedStateField(invocation, prefix + "state", used, stateProfile);
            var value = ReadUsedOption(invocation, prefix + "value", used);
            try
            {
                action = BehaviorAction.StateSet(field.Name, field.NormalizeScalar(value));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }
        }
        else
        {
            var value = ReadUsedOption(invocation, prefix + "value", used);
            action = kind switch
            {
                "KEY" => BehaviorAction.SendKey(new KeyId(value)),
                "UNICODE" => BehaviorAction.SendUnicode(value),
                "TEXT" => BehaviorAction.SendText(value),
                "SHELL" => BehaviorAction.Shell(value),
                "QUERY" => BehaviorAction.Query(value),
                "EXEC" => BehaviorAction.Exec(value, ReadIndexedOptions(invocation, prefix + "arg", used)),
                _ => throw new InvalidDataException(
                    $"Unsupported WHEN branch kind '{rawKind}'. Use key, unicode, text, exec, shell, query, set, toggle, or when.")
            };
        }
        return BehaviorOutputBranch.Action(action);
    }

    private static RuntimeStateFieldProfile ReadUsedStateField(
        BehaviorInvocationProfile invocation,
        string name,
        ISet<string> used,
        RuntimeStateProfile stateProfile)
    {
        var raw = ReadUsedOption(invocation, name, used);
        return stateProfile.GetField(raw);
    }

    private static SystemQueryConditionOperator ParseConditionOperator(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "equals" or "==" => SystemQueryConditionOperator.Equals,
            "not_equals" or "!=" => SystemQueryConditionOperator.NotEquals,
            _ => throw new InvalidDataException(
                $"Unknown condition operator '{value}'. Use equals or not_equals.")
        };

    private static string ReadUsedOption(
        BehaviorInvocationProfile invocation,
        string name,
        ISet<string> used)
    {
        if (!invocation.Options.TryGetValue(name, out var value))
            throw new InvalidDataException($"WHEN requires option '{name}'.");
        used.Add(name);
        return value;
    }

    private static void ValidateNoUnusedWhenOptions(
        BehaviorInvocationProfile invocation,
        ISet<string> used)
    {
        foreach (var option in invocation.Options.Keys)
        {
            if (!used.Contains(option))
                throw new InvalidDataException($"WHEN does not support or consume option '{option}'.");
        }
    }

    private static string ReadLiteralValue(BehaviorInvocationProfile invocation, string helper)
    {
        if (invocation.Arguments.Count == 1 && invocation.Options.Count == 0)
            return invocation.Arguments[0];

        RequireCount(invocation, 0, $"{helper}() {{ value = \"...\" }}");
        ValidateKnownOptions(invocation, ["value"]);
        return RequireOption(invocation, "value");
    }

    private static void ValidateExecOptions(BehaviorInvocationProfile invocation)
    {
        foreach (var option in invocation.Options.Keys)
        {
            if (option.Equals("executable", StringComparison.OrdinalIgnoreCase))
                continue;
            if (option.StartsWith("arg", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(option.AsSpan(3), out var index) && index >= 0)
            {
                continue;
            }

            throw new InvalidDataException($"EXEC does not support option '{option}'.");
        }
    }

    private static IReadOnlyList<string> ReadIndexedOptions(
        BehaviorInvocationProfile invocation,
        string prefix,
        ISet<string>? used = null)
    {
        var indexed = new SortedDictionary<int, string>();
        foreach (var option in invocation.Options)
        {
            if (!option.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(option.Key.AsSpan(prefix.Length), out var index) || index < 0)
            {
                continue;
            }

            indexed[index] = option.Value;
            used?.Add(option.Key);
        }

        if (indexed.Count == 0)
            return Array.Empty<string>();

        var result = new string[indexed.Count];
        var expected = 0;
        foreach (var pair in indexed)
        {
            if (pair.Key != expected)
                throw new InvalidDataException(
                    $"Arguments must use contiguous options {prefix}0..{prefix}N; missing {prefix}{expected}.");
            result[expected++] = pair.Value;
        }
        return result;
    }

    private static void RequireNoOptions(BehaviorInvocationProfile invocation)
    {
        if (invocation.Options.Count != 0)
            throw new InvalidDataException($"{invocation.Name} does not support per-instance options.");
    }

    private static string RequireOption(BehaviorInvocationProfile invocation, string name)
        => invocation.Options.TryGetValue(name, out var value)
            ? value
            : throw new InvalidDataException($"{invocation.Name} requires option '{name}'.");

    private static void RequireCount(BehaviorInvocationProfile invocation, int expected, string signature)
    {
        if (invocation.Arguments.Count != expected)
            throw new InvalidDataException($"{invocation.Name} requires {expected} argument(s): {signature}.");
    }

    private static string NormalizeModifier(string value)
        => value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => "Control",
            "SHIFT" => "Shift",
            "ALT" => "Alt",
            "GUI" or "WIN" or "SUPER" => "Gui",
            _ => throw new InvalidDataException($"Unknown modifier '{value}'.")
        };

    private static void ValidateKnownOptions(
        BehaviorInvocationProfile invocation,
        IReadOnlyCollection<string> knownNames)
    {
        foreach (var option in invocation.Options.Keys)
        {
            if (!knownNames.Contains(option, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"{invocation.Name} does not support option '{option}'.");
        }
    }

    private static int ReadDurationMs(
        BehaviorInvocationProfile invocation,
        string optionName,
        int defaultValue)
    {
        if (!invocation.Options.TryGetValue(optionName, out var raw))
            return defaultValue;

        if (!raw.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(raw.AsSpan(0, raw.Length - 2), out var value) ||
            value < 0)
        {
            throw new InvalidDataException(
                $"{invocation.Name}.{optionName} must be a non-negative duration such as '170ms'.");
        }

        return value;
    }

    private static bool ReadBoolean(
        BehaviorInvocationProfile invocation,
        string optionName,
        bool defaultValue)
    {
        if (!invocation.Options.TryGetValue(optionName, out var raw))
            return defaultValue;
        if (bool.TryParse(raw, out var value))
            return value;

        throw new InvalidDataException($"{invocation.Name}.{optionName} must be true or false.");
    }
}