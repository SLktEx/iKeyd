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
