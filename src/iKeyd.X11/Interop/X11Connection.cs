using System.Runtime.InteropServices;
using iKeyd.Core.Platform;

namespace iKeyd.X11.Interop;

public sealed class X11Connection : IBackendCapabilityProvider, IDisposable
{
    private bool _disposed;

    public X11Connection(string? displayName = null)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The X11 backend currently targets Linux.");
        Display = X11Native.XOpenDisplay(displayName);
        if (Display == 0)
            throw new InvalidOperationException($"Could not open X11 display '{displayName ?? Environment.GetEnvironmentVariable("DISPLAY") ?? "(default)"}'.");

        Screen = X11Native.XDefaultScreen(Display);
        Root = X11Native.XRootWindow(Display, Screen);
        HasXTest = X11Native.XTestQueryExtension(Display, out _, out _, out _, out _) != 0;

        var supported = new List<BackendCapability>
        {
            BackendCapability.WindowQuery,
            BackendCapability.WindowMoveResize,
            BackendCapability.WindowState,
            BackendCapability.WindowActivation,
            BackendCapability.WindowTopMost,
            BackendCapability.WindowOpacity,
            BackendCapability.PointerAbsolute
        };
        if (HasXTest)
        {
            supported.Add(BackendCapability.PointerButtons);
            supported.Add(BackendCapability.PointerScroll);
        }
        Capabilities = new BackendCapabilities(supported);
    }

    public nint Display { get; private set; }
    public int Screen { get; }
    public nuint Root { get; }
    public bool HasXTest { get; }
    public BackendCapabilities Capabilities { get; }

    public nuint Atom(string name) => X11Native.XInternAtom(Display, name, 0);

    public nuint[] GetProperty(nuint window, string propertyName, int maxItems = 4096)
    {
        ThrowIfDisposed();
        var property = Atom(propertyName);
        var status = X11Native.XGetWindowProperty(
            Display, window, property, 0, maxItems, 0, 0,
            out _, out var format, out var count, out _, out var data);
        if (status != 0 || data == 0 || count == 0)
        {
            if (data != 0) X11Native.XFree(data);
            return [];
        }

        try
        {
            var result = new nuint[(int)Math.Min(count, int.MaxValue)];
            var stride = format switch
            {
                8 => 1,
                16 => 2,
                32 => IntPtr.Size, // Xlib expands format-32 values to unsigned long.
                _ => throw new InvalidDataException($"Unsupported X11 property format {format}.")
            };
            for (var index = 0; index < result.Length; index++)
            {
                var offset = index * stride;
                result[index] = format switch
                {
                    8 => Marshal.ReadByte(data, offset),
                    16 => unchecked((nuint)(ushort)Marshal.ReadInt16(data, offset)),
                    32 when IntPtr.Size == 8 => unchecked((nuint)Marshal.ReadInt64(data, offset)),
                    32 => unchecked((nuint)(uint)Marshal.ReadInt32(data, offset)),
                    _ => 0
                };
            }
            return result;
        }
        finally
        {
            X11Native.XFree(data);
        }
    }

    public string? GetWindowClass(nuint window)
    {
        ThrowIfDisposed();
        if (X11Native.XGetClassHint(Display, window, out var hint) == 0)
            return null;
        try
        {
            return hint.ResClass != 0 ? Marshal.PtrToStringUTF8(hint.ResClass) : null;
        }
        finally
        {
            if (hint.ResName != 0) X11Native.XFree(hint.ResName);
            if (hint.ResClass != 0) X11Native.XFree(hint.ResClass);
        }
    }

    public void SetCardinal32(nuint window, string propertyName, uint value)
    {
        ThrowIfDisposed();
        var memory = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (IntPtr.Size == 8) Marshal.WriteInt64(memory, value);
            else Marshal.WriteInt32(memory, unchecked((int)value));
            X11Native.XChangeProperty(Display, window, Atom(propertyName), Atom("CARDINAL"), 32, X11Native.PropModeReplace, memory, 1);
            X11Native.XFlush(Display);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void DeleteProperty(nuint window, string propertyName)
    {
        ThrowIfDisposed();
        X11Native.XDeleteProperty(Display, window, Atom(propertyName));
        X11Native.XFlush(Display);
    }

    public void SendClientMessage(nuint window, string messageType, nint d0 = 0, nint d1 = 0, nint d2 = 0, nint d3 = 0, nint d4 = 0)
    {
        ThrowIfDisposed();
        var message = new X11Native.XClientMessageEvent
        {
            Type = X11Native.ClientMessage,
            SendEvent = 1,
            Display = Display,
            Window = window,
            MessageType = Atom(messageType),
            Format = 32,
            Data0 = d0,
            Data1 = d1,
            Data2 = d2,
            Data3 = d3,
            Data4 = d4
        };
        var memory = Marshal.AllocHGlobal(24 * IntPtr.Size);
        try
        {
            new Span<byte>((void*)memory, 24 * IntPtr.Size).Clear();
            Marshal.StructureToPtr(message, memory, false);
            X11Native.XSendEvent(Display, Root, 0, X11Native.SubstructureRedirectMask | X11Native.SubstructureNotifyMask, memory);
            X11Native.XFlush(Display);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Display != 0) X11Native.XCloseDisplay(Display);
        Display = 0;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
