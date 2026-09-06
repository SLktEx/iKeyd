using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

[Flags]
public enum KeyboardModifierMask : byte
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3
}

public readonly record struct KeyboardStateSummary(int PressedCount, KeyboardModifierMask Modifiers);

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

    /// <summary>
    /// Returns a small allocation-free summary for diagnostics/hot-path state checks.
    /// This is physical hook state, not iKeyd's transient logical layer state.
    /// </summary>
    public KeyboardStateSummary GetSummary()
    {
        lock (_gate)
        {
            var modifiers = KeyboardModifierMask.None;
            foreach (var key in _pressed)
            {
                modifiers |= key.VirtualKey switch
                {
                    0x10 or 0xA0 or 0xA1 => KeyboardModifierMask.Shift,
                    0x11 or 0xA2 or 0xA3 => KeyboardModifierMask.Control,
                    0x12 or 0xA4 or 0xA5 => KeyboardModifierMask.Alt,
                    0x5B or 0x5C => KeyboardModifierMask.Win,
                    _ => KeyboardModifierMask.None
                };
            }
            return new KeyboardStateSummary(_pressed.Count, modifiers);
        }
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
