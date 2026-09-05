using iKeyd.Core.Input;
using iKeyd.Core.Platform;

namespace iKeyd.Wayland.Input;

public sealed class LinuxUInputDevice : IKeyboardOutput, IBackendCapabilityProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly LinuxEvdevKeyMap _keyMap;
    private int _fd = -1;
    private bool _disposed;

    public LinuxUInputDevice(string path = "/dev/uinput", LinuxEvdevKeyMap? keyMap = null)
    {
        _keyMap = keyMap ?? new LinuxEvdevKeyMap();
        _fd = LinuxNative.Open(path, LinuxNative.OWriteOnly | LinuxNative.ONonBlock);
        try
        {
            Configure();
        }
        catch
        {
            LinuxNative.Close(_fd);
            _fd = -1;
            throw;
        }
    }

    public BackendCapabilities Capabilities { get; } = new([
        BackendCapability.KeyboardOutput,
        BackendCapability.TextOutputAscii,
        BackendCapability.PointerRelative,
        BackendCapability.PointerButtons,
        BackendCapability.PointerScroll,
        BackendCapability.MediaKeys
    ]);

    public void SendKey(KeyboardKey key, KeyEventKind kind)
    {
        ThrowIfDisposed();
        if (!_keyMap.TryToEvdev(key, out var code))
            throw new NotSupportedException($"No evdev output mapping exists for normalized virtual key 0x{key.VirtualKey:X2}.");
        EmitKeyCode(code, kind == KeyEventKind.Down ? 1 : 0);
    }

    public void SendKeyPress(KeyboardKey key)
    {
        SendKey(key, KeyEventKind.Down);
        SendKey(key, KeyEventKind.Up);
    }

    public void SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfDisposed();

        foreach (var character in text)
        {
            if (!_keyMap.TryGetAsciiStroke(character, out var code, out var shift))
                throw new NotSupportedException($"uinput text output currently supports ASCII keyboard strokes; U+{(int)character:X4} is not mapped.");

            lock (_gate)
            {
                if (shift)
                    WriteKeyWithoutSync(LinuxInputCodes.KeyLeftShift, 1);
                WriteKeyWithoutSync(code, 1);
                Sync();
                WriteKeyWithoutSync(code, 0);
                if (shift)
                    WriteKeyWithoutSync(LinuxInputCodes.KeyLeftShift, 0);
                Sync();
            }
        }
    }

    public bool IsToggleOn(ushort virtualKey) => false;

    public void EmitKeyCode(ushort evdevCode, int value)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            WriteKeyWithoutSync(evdevCode, value);
            Sync();
        }
    }

    public void MovePointerBy(int deltaX, int deltaY)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (deltaX != 0)
                LinuxNative.WriteInputEvent(_fd, LinuxInputCodes.EvRel, LinuxInputCodes.RelX, deltaX);
            if (deltaY != 0)
                LinuxNative.WriteInputEvent(_fd, LinuxInputCodes.EvRel, LinuxInputCodes.RelY, deltaY);
            Sync();
        }
    }

    public void SetMouseButton(ushort buttonCode, bool down)
        => EmitKeyCode(buttonCode, down ? 1 : 0);

    public void ClickMouseButton(ushort buttonCode)
    {
        EmitKeyCode(buttonCode, 1);
        EmitKeyCode(buttonCode, 0);
    }

    public void ScrollVertical(int wheelClicks)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            LinuxNative.WriteInputEvent(_fd, LinuxInputCodes.EvRel, LinuxInputCodes.RelWheel, wheelClicks);
            Sync();
        }
    }

    public void SendMediaKey(ushort keyCode)
    {
        EmitKeyCode(keyCode, 1);
        EmitKeyCode(keyCode, 0);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_fd >= 0)
            {
                try
                {
                    LinuxNative.IoctlNoArg(_fd, LinuxNative.UiDevDestroy, "UI_DEV_DESTROY");
                }
                catch
                {
                    // Closing the file descriptor still releases the virtual device.
                }
                LinuxNative.Close(_fd);
                _fd = -1;
            }
        }
        GC.SuppressFinalize(this);
    }

    private void Configure()
    {
        LinuxNative.IoctlInt(_fd, LinuxNative.UiSetEvBit, LinuxInputCodes.EvKey, "UI_SET_EVBIT(EV_KEY)");
        // Enabling all Linux key bits lets a grabbed physical keyboard pass through keys
        // that iKeyd does not normalize or remap, rather than silently dropping them.
        for (var keyCode = 0; keyCode <= LinuxNative.KeyMax; keyCode++)
            LinuxNative.IoctlInt(_fd, LinuxNative.UiSetKeyBit, keyCode, "UI_SET_KEYBIT");

        LinuxNative.IoctlInt(_fd, LinuxNative.UiSetEvBit, LinuxInputCodes.EvRel, "UI_SET_EVBIT(EV_REL)");
        LinuxNative.IoctlInt(_fd, LinuxNative.UiSetRelBit, LinuxInputCodes.RelX, "UI_SET_RELBIT(REL_X)");
        LinuxNative.IoctlInt(_fd, LinuxNative.UiSetRelBit, LinuxInputCodes.RelY, "UI_SET_RELBIT(REL_Y)");
        LinuxNative.IoctlInt(_fd, LinuxNative.UiSetRelBit, LinuxInputCodes.RelWheel, "UI_SET_RELBIT(REL_WHEEL)");
        LinuxNative.IoctlInt(_fd, LinuxNative.UiSetRelBit, LinuxInputCodes.RelHWheel, "UI_SET_RELBIT(REL_HWHEEL)");

        LinuxNative.SetupUInputDevice(_fd, "iKeyd Virtual Input");
        LinuxNative.IoctlNoArg(_fd, LinuxNative.UiDevCreate, "UI_DEV_CREATE");
    }

    private void WriteKeyWithoutSync(ushort code, int value)
        => LinuxNative.WriteInputEvent(_fd, LinuxInputCodes.EvKey, code, value);

    private void Sync()
        => LinuxNative.WriteInputEvent(_fd, LinuxInputCodes.EvSyn, LinuxInputCodes.SynReport, 0);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
