using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

public sealed class WindowsKeyboardBackend : IKeyboardInputSource, IKeyboardOutput, IDisposable
{
    private readonly WindowsKeyboardHook _hook;
    private readonly WindowsKeyboardOutput _output = new();

    public WindowsKeyboardBackend()
    {
        _hook = new WindowsKeyboardHook();
    }

    public bool IsRunning => _hook.IsRunning;
    public KeyboardState State => _hook.State;

    public void Start(IKeyboardEventHandler handler) => _hook.Start(handler);
    public void Stop() => _hook.Stop();

    public void SendKey(KeyboardKey key, KeyEventKind kind) => _output.SendKey(key, kind);
    public void SendKeyPress(KeyboardKey key) => _output.SendKeyPress(key);
    public void SendText(string text) => _output.SendText(text);
    public bool IsToggleOn(ushort virtualKey) => _output.IsToggleOn(virtualKey);

    public void Dispose() => _hook.Dispose();
}
