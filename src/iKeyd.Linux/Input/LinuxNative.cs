using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace iKeyd.Linux.Input;

internal static class LinuxNative
{
    public const int OReadOnly = 0;
    public const int OWriteOnly = 1;
    public const int ONonBlock = 0x800;
    public const int EIntr = 4;
    public const int EBadF = 9;
    public const int EAgain = 11;
    public const int InputEventSize64 = 24;
    public const ushort KeyMax = 0x2ff;

    private const byte UInputIoctlBase = (byte)'U';
    private const byte EvdevIoctlBase = (byte)'E';

    public static readonly nuint UiDevCreate = Io(UInputIoctlBase, 1);
    public static readonly nuint UiDevDestroy = Io(UInputIoctlBase, 2);
    public static readonly nuint UiDevSetup = IoW(UInputIoctlBase, 3, 92);
    public static readonly nuint UiSetEvBit = IoW(UInputIoctlBase, 100, sizeof(int));
    public static readonly nuint UiSetKeyBit = IoW(UInputIoctlBase, 101, sizeof(int));
    public static readonly nuint UiSetRelBit = IoW(UInputIoctlBase, 102, sizeof(int));
    public static readonly nuint EviocGrab = IoW(EvdevIoctlBase, 0x90, sizeof(int));

    public static int Open(string path, int flags)
    {
        EnsureLinux64();
        var fd = NativeMethods.open(path, flags);
        if (fd < 0)
            throw Error($"open('{path}') failed");
        return fd;
    }

    public static void Close(int fd)
    {
        if (fd >= 0)
            NativeMethods.close(fd);
    }

    public static void IoctlInt(int fd, nuint request, int value, string operation)
    {
        if (NativeMethods.ioctl(fd, request, unchecked((nuint)value)) < 0)
            throw Error($"{operation} failed");
    }

    public static void IoctlNoArg(int fd, nuint request, string operation)
    {
        if (NativeMethods.ioctl(fd, request, 0) < 0)
            throw Error($"{operation} failed");
    }

    public static void SetupUInputDevice(int fd, string name)
    {
        var data = new byte[92];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0, 2), 0x03);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2, 2), 0x1d6b);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), 0x0104);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), 1);
        var encoded = Encoding.UTF8.GetBytes(name);
        encoded.AsSpan(0, Math.Min(encoded.Length, 79)).CopyTo(data.AsSpan(8, 80));

        var pointer = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
            if (NativeMethods.ioctl(fd, UiDevSetup, unchecked((nuint)pointer)) < 0)
                throw Error("UI_DEV_SETUP failed");
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public static bool TryReadInputEvent(int fd, out LinuxInputEvent inputEvent, out int error)
    {
        var buffer = new byte[InputEventSize64];
        var read = NativeMethods.read(fd, buffer, (nuint)buffer.Length);
        if (read == buffer.Length)
        {
            inputEvent = new LinuxInputEvent(
                BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(0, 8)),
                BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(16, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(18, 2)),
                BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20, 4)));
            error = 0;
            return true;
        }

        inputEvent = default;
        error = read < 0 ? Marshal.GetLastPInvokeError() : 0;
        return false;
    }

    public static void WriteInputEvent(int fd, ushort type, ushort code, int value)
    {
        var buffer = new byte[InputEventSize64];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(16, 2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(18, 2), code);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20, 4), value);
        var written = NativeMethods.write(fd, buffer, (nuint)buffer.Length);
        if (written != buffer.Length)
            throw Error("write(input_event) failed");
    }

    public static Win32Exception Error(string message)
        => new(Marshal.GetLastPInvokeError(), message);

    private static void EnsureLinux64()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The Linux evdev/uinput backend requires Linux.");
        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException("The current evdev/uinput marshalling supports 64-bit Linux only.");
    }

    private static nuint Io(byte type, byte number)
        => ((nuint)type << 8) | number;

    private static nuint IoW(byte type, byte number, int size)
        => ((nuint)1 << 30) | ((nuint)size << 16) | ((nuint)type << 8) | number;

    private static class NativeMethods
    {
        [DllImport("libc", SetLastError = true)] public static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, [Out] byte[] buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, byte[] buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern int ioctl(int fd, nuint request, nuint argument);
    }
}

internal readonly record struct LinuxInputEvent(long Seconds, long Microseconds, ushort Type, ushort Code, int Value);
