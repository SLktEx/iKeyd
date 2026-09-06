using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;

namespace iKeyd.Core.Configuration;

/// <summary>
/// Build-time/profile representation of a behavior invocation such as
/// <c>LT(NUM, Z)</c>. The authoring DSL is responsible for resolving physical
/// position references before this representation reaches the runtime profile.
/// </summary>
public sealed record BehaviorInvocationProfile
{
    public BehaviorInvocationProfile(string name, IEnumerable<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Behavior name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(arguments);

        Name = name.Trim();
        Arguments = arguments.Select(argument =>
        {
            if (string.IsNullOrWhiteSpace(argument))
                throw new ArgumentException("Behavior arguments must not be empty.", nameof(arguments));
            return argument.Trim();
        }).ToArray();
    }

    public string Name { get; }
    public IReadOnlyList<string> Arguments { get; }

    public BehaviorDefinition BuildDefinition()
        => BehaviorDefinitionFactory.Create(this);
}

public sealed record BehaviorMappingProfile
{
    public BehaviorMappingProfile(KeyId key, BehaviorInvocationProfile invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        Key = key;
        Invocation = invocation;
    }

    public KeyId Key { get; }
    public BehaviorInvocationProfile Invocation { get; }
}
