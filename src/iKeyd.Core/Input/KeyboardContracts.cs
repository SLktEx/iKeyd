namespace iKeyd.Core.Input;

public enum KeyEventKind
{
    Down,
    Up
}

public enum KeyEventOrigin
{
    Physical,
    Injected,
    OwnInjected
}

public readonly record struct KeyboardKey(ushort VirtualKey, ushort ScanCode, bool IsExtended = false);

public readonly record struct KeyboardEvent(
    KeyboardKey Key,
    KeyEventKind Kind,
    KeyEventOrigin Origin,
    long TimestampMs);

public enum KeyboardDisposition
{
    PassThrough,
    Suppress
}

public interface IKeyboardEventHandler
{
    KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent);
}

/// <summary>
/// Implemented by stateful input handlers that can discard transient physical-input
/// state after suspension, hook failure, or another lifecycle discontinuity.
/// </summary>
public interface IInputStateResettable
{
    void ResetInputState();
}

public interface IKeyboardInputSource
{
    bool IsRunning { get; }
    void Start(IKeyboardEventHandler handler);
    void Stop();
}

public interface IKeyboardOutput
{
    void SendKey(KeyboardKey key, KeyEventKind kind);
    void SendKeyPress(KeyboardKey key);
    void SendText(string text);
    bool IsToggleOn(ushort virtualKey);
}

/// <summary>
/// Optional capability for backends that can emit a modifier chord as one native
/// input transaction. This keeps modifier state and the target key contiguous for
/// consumers such as terminal emulators that inspect the modifier snapshot.
/// </summary>
public interface IKeyboardChordOutput
{
    void SendChord(ReadOnlySpan<KeyboardKey> modifiers, KeyboardKey key);
}
