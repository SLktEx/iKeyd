using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

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
        => Create(invocation, new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase));

    public static BehaviorDefinition Create(
        BehaviorInvocationProfile invocation,
        IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> userDefinitions)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(userDefinitions);

        if (string.Equals(invocation.Name, "LT", StringComparison.OrdinalIgnoreCase))
            return CreateLayerTap(invocation);
        if (string.Equals(invocation.Name, "MT", StringComparison.OrdinalIgnoreCase))
            return CreateModTap(invocation);
        if (string.Equals(invocation.Name, "MO", StringComparison.OrdinalIgnoreCase))
            return CreateMomentaryLayer(invocation);
        if (string.Equals(invocation.Name, "MOD", StringComparison.OrdinalIgnoreCase))
            return CreateModifierHold(invocation);
        if (string.Equals(invocation.Name, "UNICODE", StringComparison.OrdinalIgnoreCase))
            return CreateUnicode(invocation);
        if (string.Equals(invocation.Name, "TEXT", StringComparison.OrdinalIgnoreCase))
            return CreateText(invocation);
        if (userDefinitions.TryGetValue(invocation.Name, out var userDefinition))
            return new ScriptedBehaviorDefinition(userDefinition, invocation);

        throw new NotSupportedException($"Unknown behavior '{invocation.Name}'.");
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

    private static BehaviorDefinition CreateUnicode(BehaviorInvocationProfile invocation)
        => StandardBehaviors.Unicode(ReadLiteralValue(invocation, "UNICODE"));

    private static BehaviorDefinition CreateText(BehaviorInvocationProfile invocation)
        => StandardBehaviors.Text(ReadLiteralValue(invocation, "TEXT"));

    private static string ReadLiteralValue(BehaviorInvocationProfile invocation, string helper)
    {
        if (invocation.Arguments.Count == 1 && invocation.Options.Count == 0)
            return invocation.Arguments[0];

        RequireCount(invocation, 0, $"{helper}() {{ value = \"...\" }}");
        ValidateKnownOptions(invocation, ["value"]);
        return RequireOption(invocation, "value");
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
