using System.Runtime.InteropServices;

namespace iKeyd.X11.Interop;

internal static class X11Native
{
    public const int ClientMessage = 33;
    public const long SubstructureNotifyMask = 1L << 19;
    public const long SubstructureRedirectMask = 1L << 20;
    public const int RevertToParent = 2;
    public const int PropModeReplace = 0;
    public const int AnyPropertyType = 0;
    public const int IsViewable = 2;
    public const int Button1Mask = 1 << 8;
    public const int Button2Mask = 1 << 9;
    public const int Button3Mask = 1 << 10;

    [StructLayout(LayoutKind.Sequential)]
    internal struct XWindowAttributes
    {
        public int X, Y, Width, Height, BorderWidth, Depth;
        public nint Visual;
        public nuint Root;
        public int Class, BitGravity, WinGravity, BackingStore;
        public nuint BackingPlanes, BackingPixel;
        public int SaveUnder;
        public nuint Colormap;
        public int MapInstalled, MapState;
        public long AllEventMasks, YourEventMask, DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClassHint
    {
        public nint ResName;
        public nint ResClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClientMessageEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public nint Display;
        public nuint Window;
        public nuint MessageType;
        public int Format;
        public nint Data0, Data1, Data2, Data3, Data4;
    }

    [DllImport("libX11.so.6")] internal static extern nint XOpenDisplay(string? displayName);
    [DllImport("libX11.so.6")] internal static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] internal static extern int XDefaultScreen(nint display);
    [DllImport("libX11.so.6")] internal static extern nuint XRootWindow(nint display, int screenNumber);
    [DllImport("libX11.so.6")] internal static extern nuint XInternAtom(nint display, string atomName, int onlyIfExists);
    [DllImport("libX11.so.6")] internal static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] internal static extern int XSync(nint display, int discard);
    [DllImport("libX11.so.6")] internal static extern int XFree(nint data);
    [DllImport("libX11.so.6")] internal static extern int XGetWindowAttributes(nint display, nuint window, out XWindowAttributes attributes);
    [DllImport("libX11.so.6")] internal static extern int XTranslateCoordinates(nint display, nuint source, nuint destination, int sourceX, int sourceY, out int destX, out int destY, out nuint child);
    [DllImport("libX11.so.6")] internal static extern int XMoveResizeWindow(nint display, nuint window, int x, int y, uint width, uint height);
    [DllImport("libX11.so.6")] internal static extern int XRaiseWindow(nint display, nuint window);
    [DllImport("libX11.so.6")] internal static extern int XMapRaised(nint display, nuint window);
    [DllImport("libX11.so.6")] internal static extern int XSetInputFocus(nint display, nuint focus, int revertTo, nuint time);
    [DllImport("libX11.so.6")] internal static extern int XIconifyWindow(nint display, nuint window, int screenNumber);
    [DllImport("libX11.so.6")] internal static extern int XQueryPointer(nint display, nuint window, out nuint rootReturn, out nuint childReturn, out int rootX, out int rootY, out int winX, out int winY, out uint maskReturn);
    [DllImport("libX11.so.6")] internal static extern int XWarpPointer(nint display, nuint source, nuint destination, int sourceX, int sourceY, uint sourceWidth, uint sourceHeight, int destX, int destY);
    [DllImport("libX11.so.6")] internal static extern int XGetClassHint(nint display, nuint window, out XClassHint classHint);

    [DllImport("libX11.so.6")]
    internal static extern int XGetWindowProperty(
        nint display, nuint window, nuint property, long offset, long length, int delete,
        nuint requestedType, out nuint actualType, out int actualFormat, out nuint nitems,
        out nuint bytesAfter, out nint propertyReturn);

    [DllImport("libX11.so.6")]
    internal static extern int XChangeProperty(
        nint display, nuint window, nuint property, nuint type, int format, int mode, nint data, int nelements);

    [DllImport("libX11.so.6")]
    internal static extern int XDeleteProperty(nint display, nuint window, nuint property);

    [DllImport("libX11.so.6")]
    internal static extern int XSendEvent(nint display, nuint window, int propagate, long eventMask, nint eventSend);

    [DllImport("libXtst.so.6")]
    internal static extern int XTestQueryExtension(nint display, out int eventBase, out int errorBase, out int major, out int minor);

    [DllImport("libXtst.so.6")]
    internal static extern int XTestFakeButtonEvent(nint display, uint button, int isPress, nuint delay);

    [DllImport("libXtst.so.6")]
    internal static extern int XTestFakeMotionEvent(nint display, int screenNumber, int x, int y, nuint delay);
}
