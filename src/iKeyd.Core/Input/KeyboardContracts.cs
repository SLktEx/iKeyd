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

/// <summary>
/// Describes a keyboard event. PreserveVirtualKeyWithScanCode is reserved for
/// legacy Send forms such as {vkF3sc029}: AutoHotkey supplies both wVk and wScan
/// without KEYEVENTF_SCANCODE, so Windows must not reinterpret the scan code into
/// another virtual key. Ordinary scan-code injection keeps the default false.
/// </summary>
public readonly record struct KeyboardKey(
    ushort VirtualKey,
    ushort ScanCode,
    bool IsExtended = false,
    bool PreserveVirtualKeyWithScanCode = false);

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
