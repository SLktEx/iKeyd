using System.ComponentModel;
using System.Runtime.InteropServices;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

public sealed class WindowsKeyboardOutput : IKeyboardOutput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventScanCode = 0x0008;

    public static nuint InjectionMarker { get; } = IntPtr.Size == 8
        ? unchecked((nuint)0x694B657964UL)
        : (nuint)0x694B6579U;

    public void SendKey(KeyboardKey key, KeyEventKind kind)
        => Send(BuildKeyInput(key, kind));

    public void SendKeyPress(KeyboardKey key)
        => Send(BuildKeyInput(key, KeyEventKind.Down), BuildKeyInput(key, KeyEventKind.Up));

    public void SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return;

        var inputs = new List<NativeInput>(text.Length * 2);
        foreach (var codeUnit in text)
        {
            inputs.Add(BuildUnicodeInput(codeUnit, KeyEventKind.Down));
            inputs.Add(BuildUnicodeInput(codeUnit, KeyEventKind.Up));
        }

        Send(inputs.ToArray());
    }

    public bool IsToggleOn(ushort virtualKey)
        => (NativeMethods.GetKeyState(virtualKey) & 0x0001) != 0;

    internal static NativeInput BuildKeyInput(KeyboardKey key, KeyEventKind kind)
    {
        var flags = kind == KeyEventKind.Up ? KeyEventKeyUp : 0u;
        ushort virtualKey = key.VirtualKey;
        ushort scanCode = key.ScanCode;

        if (scanCode != 0)
        {
            virtualKey = 0;
            flags |= KeyEventScanCode;
        }

        if (key.IsExtended)
            flags |= KeyEventExtended;

        return KeyboardInput(virtualKey, scanCode, flags);
    }

    internal static NativeInput BuildUnicodeInput(char codeUnit, KeyEventKind kind)
    {
        var flags = KeyEventUnicode;
        if (kind == KeyEventKind.Up)
            flags |= KeyEventKeyUp;
        return KeyboardInput(0, codeUnit, flags);
    }

    private static NativeInput KeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
        => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = InjectionMarker
                }
            }
        };

    private static void Send(params NativeInput[] inputs)
    {
        if (inputs.Length == 0)
            return;

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != (uint)inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput sent {sent} of {inputs.Length} events.");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    // INPUT contains a union of MOUSEINPUT, KEYBDINPUT and HARDWAREINPUT.
    // Including all members is important: on x64 MOUSEINPUT is 32 bytes, so the
    // union is 32 bytes and INPUT itself is 40 bytes. Defining only KEYBDINPUT
    // makes Marshal.SizeOf<INPUT>() too small and SendInput rejects the buffer.
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        [FieldOffset(0)]
        public HardwareInputData Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInputData
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint inputCount, [In] NativeInput[] inputs, int inputSize);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int virtualKey);
    }
}
