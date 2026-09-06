using System.ComponentModel;
using System.Runtime.InteropServices;
using iKeyd.Core.Desktop;

namespace iKeyd.Windows.Desktop;

public static class WindowsWindowCommand
{
    private const uint WmCommand = 0x0111;

    public static void PostCommand(WindowHandle window, uint commandId)
    {
        if (window.IsEmpty)
            throw new ArgumentException("Window handle is empty.", nameof(window));

        if (!NativeMethods.PostMessageW(window.Value, WmCommand, commandId, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "PostMessage(WM_COMMAND) failed.");
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    }
}
