namespace iKeyd.Core.Automation;

public interface ISystemQuerySnapshot
{
    bool TryGetValue(string key, out string value);
}

/// <summary>
/// Lock-free snapshot publication for the keyboard path. Refreshers build a fresh
/// dictionary off-path and publish one reference atomically; readers only perform
/// a dictionary lookup.
/// </summary>
public sealed class SystemQuerySnapshotStore : ISystemQuerySnapshot
{
    private Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetValue(string key, out string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = string.Empty;
            return false;
        }

        return Volatile.Read(ref _values).TryGetValue(key, out value!);
    }

    public void Publish(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            var key = SystemQueryKeys.Normalize(pair.Key);
            next[key] = pair.Value ?? string.Empty;
        }
        Volatile.Write(ref _values, next);
    }
}

public sealed class EmptySystemQuerySnapshot : ISystemQuerySnapshot
{
    private EmptySystemQuerySnapshot() { }
    public static EmptySystemQuerySnapshot Instance { get; } = new();

    public bool TryGetValue(string key, out string value)
    {
        value = string.Empty;
        return false;
    }
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
