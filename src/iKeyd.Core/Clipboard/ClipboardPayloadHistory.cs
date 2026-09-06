namespace iKeyd.Core.Clipboard;

public interface IClipboardPayloadService
{
    ClipboardPayload? ReadPayload();
    void WritePayload(ClipboardPayload payload);
}

public interface IClipboardPayloadPicker
{
    int? Pick(IReadOnlyList<ClipboardPayload> items);
}

public interface IClipboardHistoryPersistence
{
    IReadOnlyList<ClipboardPayload> Load();
    void Save(IReadOnlyList<ClipboardPayload> items);
    void Clear();
}

/// <summary>
/// Binary-safe clipboard history used by the persisted Win+V-like history.
/// The legacy string-only ClipboardHistory remains available for hotkeySKG
/// compatibility while this history carries text and images without conversion.
/// </summary>
public sealed class ClipboardPayloadHistory
{
    private readonly object _gate = new();
    private readonly List<ClipboardPayload> _items = [];
    private readonly IClipboardHistoryPersistence? _persistence;

    public ClipboardPayloadHistory(
        int capacity = 20,
        IClipboardHistoryPersistence? persistence = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        Capacity = capacity;
        _persistence = persistence;

        if (_persistence is not null)
        {
            foreach (var item in _persistence.Load().Take(capacity))
                _items.Add(item);
        }
    }

    public int Capacity { get; }

    public IReadOnlyList<ClipboardPayload> Items
    {
        get
        {
            lock (_gate)
                return _items.ToArray();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _items.Count;
        }
    }

    public bool Record(ClipboardPayload? payload)
    {
        if (payload is null || payload.Data.Length == 0)
            return false;

        lock (_gate)
        {
            var existing = _items.FindIndex(item => PayloadEquals(item, payload));
            if (existing == 0)
                return false;
            if (existing > 0)
                _items.RemoveAt(existing);

            _items.Insert(0, payload);
            if (_items.Count > Capacity)
                _items.RemoveRange(Capacity, _items.Count - Capacity);
            PersistLocked();
            return true;
        }
    }

    public ClipboardPayload Promote(int index)
    {
        lock (_gate)
        {
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var payload = _items[index];
            if (index != 0)
            {
                _items.RemoveAt(index);
                _items.Insert(0, payload);
                PersistLocked();
            }
            return payload;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _persistence?.Clear();
        }
    }

    private static bool PayloadEquals(ClipboardPayload left, ClipboardPayload right)
        => left.Kind == right.Kind
           && string.Equals(left.ContentType, right.ContentType, StringComparison.OrdinalIgnoreCase)
           && left.Data.AsSpan().SequenceEqual(right.Data);

    private void PersistLocked()
        => _persistence?.Save(_items);
}
