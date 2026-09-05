namespace iKeyd.Core.Clipboard;

public interface IClipboardService : IDisposable
{
    event EventHandler? Changed;
    string? ReadText();
    void WriteText(string text);
}

public interface IClipboardPicker
{
    int? Pick(IReadOnlyList<string> items);
}

public sealed class ClipboardHistory
{
    private readonly object _gate = new();
    private readonly List<string> _items = [];

    public ClipboardHistory(int capacity = 20)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }

    public IReadOnlyList<string> Items
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

    /// <summary>
    /// Records a clipboard-change notification using the legacy hotkeySKG
    /// semantics: every non-empty change is prepended, even when the same text
    /// already exists elsewhere (or is already the current first item).
    /// </summary>
    public bool Record(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        lock (_gate)
        {
            _items.Insert(0, text);
            if (_items.Count > Capacity)
                _items.RemoveRange(Capacity, _items.Count - Capacity);
            return true;
        }
    }

    /// <summary>
    /// Promotes the exact selected row, not the first equal string. This mirrors
    /// hotkeySKG's clipNTmp/index-based picker behavior and therefore preserves
    /// duplicate clipboard entries faithfully.
    /// </summary>
    public string Promote(int index)
    {
        lock (_gate)
        {
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var text = _items[index];
            if (index != 0)
            {
                _items.RemoveAt(index);
                _items.Insert(0, text);
            }
            return text;
        }
    }

    public void Clear()
    {
        lock (_gate)
            _items.Clear();
    }
}
