namespace iKeyd.Core.Automation;

public interface ISystemQuerySnapshot
{
    bool TryGetValue(string key, out string value);
}

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
