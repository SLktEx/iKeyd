using System.Buffers;
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
    private const int MaxStackTextLength = 128;

    public static nuint InjectionMarker { get; } = IntPtr.Size == 8
        ? unchecked((nuint)0x694B657964UL)
        : (nuint)0x694B6579U;

    public void SendKey(KeyboardKey key, KeyEventKind kind)
    {
        Span<NativeInput> inputs = stackalloc NativeInput[1];
        inputs[0] = BuildKeyInput(key, kind);
        Send(inputs);
    }

    public void SendKeyPress(KeyboardKey key)
    {
        Span<NativeInput> inputs = stackalloc NativeInput[2];
        inputs[0] = BuildKeyInput(key, KeyEventKind.Down);
        inputs[1] = BuildKeyInput(key, KeyEventKind.Up);
        Send(inputs);
    }

    public void SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return;

        var inputCount = checked(text.Length * 2);
        if (text.Length <= MaxStackTextLength)
        {
            Span<NativeInput> inputs = stackalloc NativeInput[inputCount];
            FillUnicodeInputs(text, inputs);
            Send(inputs);
            return;
        }

        var rented = ArrayPool<NativeInput>.Shared.Rent(inputCount);
        try
        {
            var inputs = rented.AsSpan(0, inputCount);
            FillUnicodeInputs(text, inputs);
            Send(inputs);
        }
        finally
        {
            ArrayPool<NativeInput>.Shared.Return(rented, clearArray: false);
        }
    }

    public bool IsToggleOn(ushort virtualKey)
        => (NativeMethods.GetKeyState(virtualKey) & 0x0001) != 0;

    internal static NativeInput BuildKeyInput(KeyboardKey key, KeyEventKind kind)
    {
        var flags = kind == KeyEventKind.Up ? KeyEventKeyUp : 0u;
        ushort virtualKey = key.VirtualKey;
        ushort scanCode = key.ScanCode;

        // Generic scan-code injection asks Windows to derive the VK from the scan
        // code. Explicit AHK vk+sc tokens instead populate both fields while
        // leaving KEYEVENTF_SCANCODE clear; WH_KEYBOARD_LL then observes the same
        // VK/scan identity as the legacy executable.
        if (scanCode != 0 && !key.PreserveVirtualKeyWithScanCode)
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

    private static void FillUnicodeInputs(string text, Span<NativeInput> inputs)
    {
        var index = 0;
        foreach (var codeUnit in text)
        {
            inputs[index++] = BuildUnicodeInput(codeUnit, KeyEventKind.Down);
            inputs[index++] = BuildUnicodeInput(codeUnit, KeyEventKind.Up);
        }
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

    private static unsafe void Send(ReadOnlySpan<NativeInput> inputs)
    {
        if (inputs.Length == 0)
            return;

        fixed (NativeInput* pointer = inputs)
        {
            var sent = NativeMethods.SendInput((uint)inputs.Length, pointer, sizeof(NativeInput));
            if (sent != (uint)inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput sent {sent} of {inputs.Length} events.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

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
        public static extern unsafe uint SendInput(uint inputCount, NativeInput* inputs, int inputSize);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int virtualKey);
    }
}
