using System.Runtime.InteropServices;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

public sealed class WindowsInputMethod : IInputMethod
{
    private const uint WmImeControl = 0x0283;
    private const int ImcGetConversionMode = 0x0001;
    private const int ImcGetOpenStatus = 0x0005;

    public bool IsKanaInputActive()
    {
        var target = GetFocusedWindow();
        if (target == 0)
            return false;

        var imeWindow = NativeMethods.ImmGetDefaultIMEWnd(target);
        if (imeWindow == 0)
            return false;

        var open = NativeMethods.SendMessageW(imeWindow, WmImeControl, (nint)ImcGetOpenStatus, 0);
        if (open != 1)
            return false;

        var conversionMode = (int)NativeMethods.SendMessageW(
            imeWindow,
            WmImeControl,
            (nint)ImcGetConversionMode,
            0);

        return IsRomaKanaConversionMode(conversionMode);
    }

    public static bool IsRomaKanaConversionMode(int conversionMode)
        => conversionMode is 9 or 19 or 25 or 27 or 16;

    private static nint GetFocusedWindow()
    {
        var info = new GuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInfo>()
        };

        if (NativeMethods.GetGUIThreadInfo(0, ref info) && info.Focus != 0)
            return info.Focus;

        return NativeMethods.GetForegroundWindow();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("imm32.dll")]
        public static extern nint ImmGetDefaultIMEWnd(nint window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    }
}
