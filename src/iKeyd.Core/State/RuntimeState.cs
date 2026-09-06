namespace iKeyd.Core.State;

public enum RuntimeStateType
{
    Bool,
    String
}

public sealed record RuntimeStateFieldProfile
{
    private RuntimeStateFieldProfile(
        string name,
        RuntimeStateType type,
        bool initialBool,
        string? initialString)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Runtime state field name must not be empty.", nameof(name));

        Name = name.Trim();
        Type = type;
        InitialBool = initialBool;
        InitialString = initialString;
    }

    public string Name { get; }
    public RuntimeStateType Type { get; }
    public bool InitialBool { get; }
    public string? InitialString { get; }

    public string InitialScalar
        => Type == RuntimeStateType.Bool
            ? (InitialBool ? "true" : "false")
            : InitialString ?? string.Empty;

    public static RuntimeStateFieldProfile Bool(string name, bool initialValue = false)
        => new(name, RuntimeStateType.Bool, initialValue, null);

    public static RuntimeStateFieldProfile String(string name, string initialValue)
        => new(
            name,
            RuntimeStateType.String,
            false,
            initialValue ?? throw new ArgumentNullException(nameof(initialValue)));
}

public sealed class RuntimeStateProfile
{
    private readonly IReadOnlyList<RuntimeStateFieldProfile> _fields;
    private readonly IReadOnlyDictionary<string, RuntimeStateFieldProfile> _byName;

    public RuntimeStateProfile(IEnumerable<RuntimeStateFieldProfile>? fields = null)
    {
        var list = new List<RuntimeStateFieldProfile>();
        var byName = new Dictionary<string, RuntimeStateFieldProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields ?? [])
        {
            ArgumentNullException.ThrowIfNull(field);
            if (!byName.TryAdd(field.Name, field))
                throw new ArgumentException($"Duplicate runtime state field '{field.Name}'.", nameof(fields));
            list.Add(field);
        }

        _fields = list;
        _byName = byName;
    }

    public static RuntimeStateProfile Empty { get; } = new();

    public IReadOnlyList<RuntimeStateFieldProfile> Fields => _fields;
    public int Count => _fields.Count;

    public bool TryGetField(string name, out RuntimeStateFieldProfile field)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            field = null!;
            return false;
        }
        return _byName.TryGetValue(name.Trim(), out field!);
    }

    public RuntimeStateFieldProfile GetField(string name)
        => TryGetField(name, out var field)
            ? field
            : throw new KeyNotFoundException($"Runtime state does not define field '{name}'.");
}

public interface IRuntimeStateSnapshot
{
    bool TryGetScalar(string fieldName, out string value);
}

public interface IRuntimeStateStore : IRuntimeStateSnapshot
{
    void SetScalar(string fieldName, string value);
    void Toggle(string fieldName);
    void Reset();
}

public sealed class EmptyRuntimeStateStore : IRuntimeStateStore
{
    private EmptyRuntimeStateStore() { }
    public static EmptyRuntimeStateStore Instance { get; } = new();

    public bool TryGetScalar(string fieldName, out string value)
    {
        value = string.Empty;
        return false;
    }

    public void SetScalar(string fieldName, string value)
        => throw new KeyNotFoundException($"Runtime state does not define field '{fieldName}'.");

    public void Toggle(string fieldName)
        => throw new KeyNotFoundException($"Runtime state does not define field '{fieldName}'.");

    public void Reset() { }
}

/// <summary>
/// Fixed-shape process-local shared state. Field descriptors are immutable after
/// profile compilation; bool slots use Interlocked and string slots use volatile
/// reference publication so ordinary reads/writes require no platform I/O and no
/// unbounded lock contention on the input path.
/// </summary>
public sealed class RuntimeStateStore : IRuntimeStateStore
{
    private sealed record Descriptor(RuntimeStateFieldProfile Field, int Slot);

    private readonly RuntimeStateProfile _profile;
    private readonly Dictionary<string, Descriptor> _descriptors;
    private readonly int[] _boolValues;
    private readonly string?[] _stringValues;

    public RuntimeStateStore(RuntimeStateProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _descriptors = new Dictionary<string, Descriptor>(StringComparer.OrdinalIgnoreCase);

        var boolCount = profile.Fields.Count(field => field.Type == RuntimeStateType.Bool);
        var stringCount = profile.Fields.Count - boolCount;
        _boolValues = new int[boolCount];
        _stringValues = new string?[stringCount];

        var boolSlot = 0;
        var stringSlot = 0;
        foreach (var field in profile.Fields)
        {
            var slot = field.Type == RuntimeStateType.Bool ? boolSlot++ : stringSlot++;
            _descriptors.Add(field.Name, new Descriptor(field, slot));
        }

        Reset();
    }

    public RuntimeStateProfile Profile => _profile;

    public bool TryGetScalar(string fieldName, out string value)
    {
        if (!TryGetDescriptor(fieldName, out var descriptor))
        {
            value = string.Empty;
            return false;
        }

        if (descriptor.Field.Type == RuntimeStateType.Bool)
        {
            value = Volatile.Read(ref _boolValues[descriptor.Slot]) != 0 ? "true" : "false";
            return true;
        }

        value = Volatile.Read(ref _stringValues[descriptor.Slot]) ?? string.Empty;
        return true;
    }

    public void SetScalar(string fieldName, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var descriptor = GetDescriptor(fieldName);
        if (descriptor.Field.Type == RuntimeStateType.Bool)
        {
            if (!bool.TryParse(value, out var parsed))
            {
                throw new ArgumentException(
                    $"Runtime state field '{descriptor.Field.Name}' is bool and requires true or false.",
                    nameof(value));
            }
            Volatile.Write(ref _boolValues[descriptor.Slot], parsed ? 1 : 0);
            return;
        }

        Volatile.Write(ref _stringValues[descriptor.Slot], value);
    }

    public void Toggle(string fieldName)
    {
        var descriptor = GetDescriptor(fieldName);
        if (descriptor.Field.Type != RuntimeStateType.Bool)
            throw new InvalidOperationException($"Runtime state field '{descriptor.Field.Name}' is not bool and cannot be toggled.");

        ref var slot = ref _boolValues[descriptor.Slot];
        while (true)
        {
            var current = Volatile.Read(ref slot);
            if (Interlocked.CompareExchange(ref slot, current == 0 ? 1 : 0, current) == current)
                return;
        }
    }

    public void Reset()
    {
        foreach (var descriptor in _descriptors.Values)
        {
            if (descriptor.Field.Type == RuntimeStateType.Bool)
            {
                Volatile.Write(
                    ref _boolValues[descriptor.Slot],
                    descriptor.Field.InitialBool ? 1 : 0);
            }
            else
            {
                Volatile.Write(
                    ref _stringValues[descriptor.Slot],
                    descriptor.Field.InitialString ?? string.Empty);
            }
        }
    }

    private bool TryGetDescriptor(string fieldName, out Descriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            descriptor = null!;
            return false;
        }
        return _descriptors.TryGetValue(fieldName.Trim(), out descriptor!);
    }

    private Descriptor GetDescriptor(string fieldName)
        => TryGetDescriptor(fieldName, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Runtime state does not define field '{fieldName}'.");
}
