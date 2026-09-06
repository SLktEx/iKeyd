namespace iKeyd.Core.Configuration;

/// <summary>
/// Static profile representation of a user-defined behavior. The authoring DSL
/// lowers source handlers into this bounded statement IR before runtime startup.
/// </summary>
public sealed record UserBehaviorDefinitionProfile
{
    public UserBehaviorDefinitionProfile(
        string name,
        IEnumerable<string> parameters,
        IEnumerable<UserBehaviorLocalProfile>? locals = null,
        IEnumerable<UserBehaviorHandlerProfile>? handlers = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("User behavior name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(parameters);

        Name = name.Trim();
        Parameters = NormalizeUniqueNames(parameters, nameof(parameters));
        Locals = (locals ?? []).ToArray();
        Handlers = (handlers ?? []).ToArray();

        var localNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var local in Locals)
        {
            ArgumentNullException.ThrowIfNull(local);
            if (!localNames.Add(local.Name))
                throw new ArgumentException($"Duplicate local '{local.Name}' in behavior '{Name}'.", nameof(locals));
            if (Parameters.Contains(local.Name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Local '{local.Name}' conflicts with a parameter in behavior '{Name}'.", nameof(locals));
        }

        var handlerEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in Handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (!handlerEvents.Add(handler.Event))
                throw new ArgumentException($"Duplicate handler '{handler.Event}' in behavior '{Name}'.", nameof(handlers));
        }
    }

    public string Name { get; }
    public IReadOnlyList<string> Parameters { get; }
    public IReadOnlyList<UserBehaviorLocalProfile> Locals { get; }
    public IReadOnlyList<UserBehaviorHandlerProfile> Handlers { get; }

    public UserBehaviorHandlerProfile? FindHandler(string eventName)
        => Handlers.FirstOrDefault(handler =>
            string.Equals(handler.Event, eventName, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> NormalizeUniqueNames(
        IEnumerable<string> values,
        string parameterName)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Names must not be empty.", parameterName);
            var normalized = value.Trim();
            if (!seen.Add(normalized))
                throw new ArgumentException($"Duplicate name '{normalized}'.", parameterName);
            result.Add(normalized);
        }
        return result;
    }
}

public enum UserBehaviorLocalType
{
    Bool,
    Int
}

public sealed record UserBehaviorLocalProfile
{
    public UserBehaviorLocalProfile(string name, bool initialValue = false)
    {
        Name = NormalizeName(name);
        Type = UserBehaviorLocalType.Bool;
        InitialBoolValue = initialValue;
    }

    public UserBehaviorLocalProfile(string name, int initialValue)
    {
        Name = NormalizeName(name);
        Type = UserBehaviorLocalType.Int;
        InitialIntValue = initialValue;
    }

    public string Name { get; }
    public UserBehaviorLocalType Type { get; }

    /// <summary>
    /// Backward-compatible bool initializer used by existing profile/compiler code.
    /// Integer locals expose their value through <see cref="InitialIntValue"/>.
    /// </summary>
    public bool InitialValue => InitialBoolValue;
    public bool InitialBoolValue { get; }
    public int InitialIntValue { get; }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Local name must not be empty.", nameof(name));
        return name.Trim();
    }
}

public sealed record UserBehaviorHandlerProfile
{
    public UserBehaviorHandlerProfile(
        string eventName,
        IEnumerable<string>? parameters,
        IEnumerable<UserBehaviorStatementProfile> statements)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Handler event must not be empty.", nameof(eventName));
        ArgumentNullException.ThrowIfNull(statements);

        Event = eventName.Trim();
        Parameters = (parameters ?? []).Select(parameter =>
        {
            if (string.IsNullOrWhiteSpace(parameter))
                throw new ArgumentException("Handler parameter must not be empty.", nameof(parameters));
            return parameter.Trim();
        }).ToArray();
        Statements = statements.ToArray();
    }

    public string Event { get; }
    public IReadOnlyList<string> Parameters { get; }
    public IReadOnlyList<UserBehaviorStatementProfile> Statements { get; }
}

/// <summary>
/// Deliberately small, bounded statement IR for custom behaviors. Operations are
/// compile-time validated; this is not a general-purpose scripting language.
/// </summary>
public sealed record UserBehaviorStatementProfile
{
    public UserBehaviorStatementProfile(
        string op,
        string? target = null,
        string? value = null,
        string? condition = null,
        IEnumerable<UserBehaviorStatementProfile>? thenStatements = null,
        IEnumerable<UserBehaviorStatementProfile>? elseStatements = null)
    {
        if (string.IsNullOrWhiteSpace(op))
            throw new ArgumentException("Statement operation must not be empty.", nameof(op));

        Op = op.Trim();
        Target = target?.Trim();
        Value = value?.Trim();
        Condition = condition?.Trim();
        Then = (thenStatements ?? []).ToArray();
        Else = (elseStatements ?? []).ToArray();
    }

    public string Op { get; }
    public string? Target { get; }
    public string? Value { get; }
    public string? Condition { get; }
    public IReadOnlyList<UserBehaviorStatementProfile> Then { get; }
    public IReadOnlyList<UserBehaviorStatementProfile> Else { get; }
}
