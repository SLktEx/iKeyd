using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

public sealed class KeyboardState
{
    private readonly object _gate = new();
    private readonly HashSet<KeyboardKey> _pressed = [];

    public void Apply(KeyboardEvent keyboardEvent)
    {
        lock (_gate)
        {
            if (keyboardEvent.Kind == KeyEventKind.Down)
                _pressed.Add(keyboardEvent.Key);
            else
                _pressed.Remove(keyboardEvent.Key);
        }
    }

    public bool IsPressed(KeyboardKey key)
    {
        lock (_gate)
            return _pressed.Contains(key);
    }

    public bool IsVirtualKeyPressed(ushort virtualKey)
    {
        lock (_gate)
            return _pressed.Any(key => key.VirtualKey == virtualKey);
    }

    public IReadOnlyList<KeyboardKey> Snapshot()
    {
        lock (_gate)
            return _pressed.ToArray();
    }

    public void Clear()
    {
        lock (_gate)
            _pressed.Clear();
    }
}
