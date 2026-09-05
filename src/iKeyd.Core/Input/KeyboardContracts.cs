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
