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
        key = NormalizeIdentityReplayKey(key);
        if (UsesCombinedVirtualScanPath(key))
        {
            SendCombinedVirtualScanKey(key, kind);
            return;
        }

        Span<NativeInput> inputs = stackalloc NativeInput[1];
        inputs[0] = BuildKeyInput(key, kind);
        Send(inputs);
    }

    public void SendKeyPress(KeyboardKey key)
    {
        key = NormalizeIdentityReplayKey(key);
        if (UsesCombinedVirtualScanPath(key))
        {
            SendCombinedVirtualScanKey(key, KeyEventKind.Down);
            SendCombinedVirtualScanKey(key, KeyEventKind.Up);
            return;
        }

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

    /// <summary>
    /// Some compatibility paths consume a physical key and then replay the same
    /// key identity from a VK-only representation. Real JIS verification found
    /// that VK-only SendInput is not sufficient for number/function rows and also
    /// loses the physical JIS punctuation semantics needed by Japanese IME.
    /// Recreate those identities as set-1 scan-code input instead.
    ///
    /// Keep ordinary romaji/new-shita letter output on the existing VK path, and
    /// keep explicit legacy vk+sc pairs on their pair-preserving compatibility path.
    /// </summary>
    internal static KeyboardKey NormalizeIdentityReplayKey(KeyboardKey key)
    {
        if (key.ScanCode != 0 || key.VirtualKey == 0)
            return key;

        var scanCode = IdentityReplayScanCode(key.VirtualKey);
        return scanCode == 0
            ? key
            : new KeyboardKey(0, scanCode, key.IsExtended);
    }

    internal static bool UsesCombinedVirtualScanPath(KeyboardKey key)
        => key.VirtualKey is > 0 and <= byte.MaxValue &&
           key.ScanCode is > 0 and <= byte.MaxValue;

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

    internal static void FillUnicodeInputs(string text, Span<NativeInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (inputs.Length != checked(text.Length * 2))
            throw new ArgumentException("Unicode input buffer must contain exactly two events per UTF-16 code unit.", nameof(inputs));

        var index = 0;
        foreach (var codeUnit in text)
        {
            inputs[index++] = BuildUnicodeInput(codeUnit, KeyEventKind.Down);
            inputs[index++] = BuildUnicodeInput(codeUnit, KeyEventKind.Up);
        }
    }

    private static ushort IdentityReplayScanCode(ushort virtualKey)
    {
        if (virtualKey is >= '1' and <= '9')
            return checked((ushort)(0x02 + virtualKey - '1'));
        if (virtualKey == '0')
            return 0x0B;

        if (virtualKey is >= 0x70 and <= 0x79) // F1-F10
            return checked((ushort)(0x3B + virtualKey - 0x70));
        if (virtualKey == 0x7A) // F11
            return 0x57;
        if (virtualKey == 0x7B) // F12
            return 0x58;

        // JIS106/109 punctuation positions. These must be replayed as physical
        // scan codes so Microsoft IME can apply the same punctuation semantics as
        // a real key press instead of receiving a layout-agnostic VK-only event.
        return virtualKey switch
        {
            0xBD => 0x0C, // - / =
            0xDE => 0x0D, // ^ / ~
            0xDC => 0x7D, // Yen / backslash
            0xC0 => 0x1A, // @ / `
            0xDB => 0x1B, // [ / {
            0xBB => 0x27, // ; / +
            0xBA => 0x28, // : / *
            0xDD => 0x2B, // ] / }
            0xBC => 0x33, // , / <
            0xBE => 0x34, // . / >
            0xBF => 0x35, // / / ?
            0xE2 => 0x73, // Ro / _
            _ => 0
        };
    }

    private static void SendCombinedVirtualScanKey(KeyboardKey key, KeyEventKind kind)
    {
        // AutoHotkey's {vkXXscYYY} notation explicitly represents a key event with
        // both values. KEYEVENTF_SCANCODE makes SendInput ignore wVk, so the normal
        // SendInput representation cannot preserve that pair. keybd_event accepts
        // both bVk and bScan and is retained here only for this legacy-compat path.
        var flags = key.IsExtended ? KeyEventExtended : 0u;
        if (kind == KeyEventKind.Up)
            flags |= KeyEventKeyUp;
        NativeMethods.keybd_event((byte)key.VirtualKey, (byte)key.ScanCode, flags, InjectionMarker);
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
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int virtualKey);
    }
}
