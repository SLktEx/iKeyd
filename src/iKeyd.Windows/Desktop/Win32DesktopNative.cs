using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using iKeyd.Core.Desktop;
using iKeyd.Windows.Input;

namespace iKeyd.Windows.Desktop;

internal interface IWindowsDesktopNative
{
    nint GetForegroundWindow();
    bool IsIconic(nint window);
    bool IsZoomed(nint window);
    bool GetWindowRect(nint window, out NativeRect rect);
    bool TryGetPrimaryWorkArea(out NativeRect rect);
    string? GetWindowClass(nint window);
    bool IsWindow(nint window);
    bool ShowWindow(nint window, int command);
    bool MoveWindow(nint window, int x, int y, int width, int height);
    bool SetForegroundWindow(nint window);
    IReadOnlyList<nint> EnumerateTopLevelWindows();
    nint GetWindowLongPtr(nint window, int index);
    nint SetWindowLongPtr(nint window, int index, nint value);
    void ClearLastError();
    int GetLastError();
    bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    bool GetLayeredWindowAttributes(nint window, out byte alpha, out uint flags);
    bool SetLayeredWindowAttributes(nint window, byte alpha, uint flags);
    bool GetCursorPos(out NativePoint point);
    bool SetCursorPos(int x, int y);
    short GetAsyncKeyState(int virtualKey);
    void SendMouseMove(int deltaX, int deltaY);
    void SendMouseButton(DesktopMouseButton button, bool down);
    void SendMouseWheel(int wheelDelta);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativePoint
{
    public NativePoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRect
{
    public NativeRect(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }

    public DesktopRect ToDesktopRect() => new(Left, Top, Right - Left, Bottom - Top);
}

internal sealed class Win32DesktopNative : IWindowsDesktopNative
{
    private const uint SpiGetWorkArea = 0x0030;
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();
    public bool IsIconic(nint window) => NativeMethods.IsIconic(window);
    public bool IsZoomed(nint window) => NativeMethods.IsZoomed(window);
    public bool GetWindowRect(nint window, out NativeRect rect) => NativeMethods.GetWindowRect(window, out rect);
    public bool TryGetPrimaryWorkArea(out NativeRect rect) => NativeMethods.SystemParametersInfo(SpiGetWorkArea, 0, out rect, 0);
    public bool IsWindow(nint window) => NativeMethods.IsWindow(window);
    public bool ShowWindow(nint window, int command) => NativeMethods.ShowWindow(window, command);
    public bool MoveWindow(nint window, int x, int y, int width, int height)
        => NativeMethods.MoveWindow(window, x, y, width, height, true);
    public bool SetForegroundWindow(nint window) => NativeMethods.SetForegroundWindow(window);
    public nint GetWindowLongPtr(nint window, int index)
        => IntPtr.Size == 8 ? NativeMethods.GetWindowLongPtr64(window, index) : new nint(NativeMethods.GetWindowLong32(window, index));
    public nint SetWindowLongPtr(nint window, int index, nint value)
        => IntPtr.Size == 8 ? NativeMethods.SetWindowLongPtr64(window, index, value) : new nint(NativeMethods.SetWindowLong32(window, index, value.ToInt32()));
    public void ClearLastError() => Marshal.SetLastPInvokeError(0);
    public int GetLastError() => Marshal.GetLastPInvokeError();
    public bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags)
        => NativeMethods.SetWindowPos(window, insertAfter, x, y, width, height, flags);

    public string? GetWindowClass(nint window)
    {
        var builder = new StringBuilder(256);
        return NativeMethods.GetClassName(window, builder, builder.Capacity) == 0 ? null : builder.ToString();
    }

    public IReadOnlyList<nint> EnumerateTopLevelWindows()
    {
        var windows = new List<nint>();
        if (!NativeMethods.EnumWindows((window, _) =>
            {
                windows.Add(window);
                return true;
            }, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "EnumWindows failed.");
        return windows;
    }

    public bool GetLayeredWindowAttributes(nint window, out byte alpha, out uint flags)
        => NativeMethods.GetLayeredWindowAttributes(window, out _, out alpha, out flags);

    public bool SetLayeredWindowAttributes(nint window, byte alpha, uint flags)
        => NativeMethods.SetLayeredWindowAttributes(window, 0, alpha, flags);

    public bool GetCursorPos(out NativePoint point) => NativeMethods.GetCursorPos(out point);
    public bool SetCursorPos(int x, int y) => NativeMethods.SetCursorPos(x, y);
    public short GetAsyncKeyState(int virtualKey) => NativeMethods.GetAsyncKeyState(virtualKey);

    public void SendMouseMove(int deltaX, int deltaY)
        => SendMouse(MouseEventMove, 0, deltaX, deltaY);

    public void SendMouseButton(DesktopMouseButton button, bool down)
    {
        var flags = (button, down) switch
        {
            (DesktopMouseButton.Left, true) => MouseEventLeftDown,
            (DesktopMouseButton.Left, false) => MouseEventLeftUp,
            (DesktopMouseButton.Right, true) => MouseEventRightDown,
            (DesktopMouseButton.Right, false) => MouseEventRightUp,
            (DesktopMouseButton.Middle, true) => MouseEventMiddleDown,
            (DesktopMouseButton.Middle, false) => MouseEventMiddleUp,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };
        SendMouse(flags, 0);
    }

    public void SendMouseWheel(int wheelDelta)
        => SendMouse(MouseEventWheel, unchecked((uint)wheelDelta));

    private static void SendMouse(uint flags, uint data, int x = 0, int y = 0)
    {
        var input = new NativeInput
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInputData
                {
                    X = x,
                    Y = y,
                    MouseData = data,
                    Flags = flags,
                    ExtraInfo = WindowsKeyboardOutput.InjectionMarker
                }
            }
        };
        var sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeInput>());
        if (sent != 1)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SendInput failed for mouse event.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
        [FieldOffset(0)] public HardwareInputData Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInputData
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(nint window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint window, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint action, uint parameter, out NativeRect value, uint update);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint window, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(nint window, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        public static extern int GetWindowLong32(nint window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern nint GetWindowLongPtr64(nint window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        public static extern int SetWindowLong32(nint window, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        public static extern nint SetWindowLongPtr64(nint window, int index, nint value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetLayeredWindowAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint inputCount, [In] NativeInput[] inputs, int inputSize);
    }
}
