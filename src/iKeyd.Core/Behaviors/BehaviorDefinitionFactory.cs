using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// Converts the profile/IR representation of standard behavior invocations into
/// executable behavior definitions. User-defined behavior definitions will be
/// added to this compilation boundary later; the event runtime remains generic.
/// </summary>
public static class BehaviorDefinitionFactory
{
    public static BehaviorDefinition Create(BehaviorInvocationProfile invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (string.Equals(invocation.Name, "LT", StringComparison.OrdinalIgnoreCase))
            return CreateLayerTap(invocation);

        throw new NotSupportedException($"Unknown behavior '{invocation.Name}'.");
    }

    private static BehaviorDefinition CreateLayerTap(BehaviorInvocationProfile invocation)
    {
        if (invocation.Arguments.Count != 2)
            throw new InvalidDataException("LT requires exactly two arguments: LT(layer, tap_key).");

        var layer = invocation.Arguments[0];
        var tapKey = new KeyId(invocation.Arguments[1]);
        return StandardBehaviors.LT(layer, tapKey);
    }
}
