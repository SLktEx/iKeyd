using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace iKeyd.Windows.Clipboard;

/// <summary>
/// Windows Imaging Component (WIC) decoder boundary used by Clipboard History.
/// WIC discovers installed codecs at runtime, so formats such as WebP/HEIF can
/// become decodable without adding format-specific branches or bundled codecs.
/// </summary>
internal static class WindowsWicImageDecoder
{
    private static readonly Guid ClsidWicImagingFactory =
        new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid PixelFormat32BppBgra =
        new("6fddc324-4e03-4bfe-b185-3d77768dc90f");

    private const int EFail = unchecked((int)0x80004005);

    public static bool TryDecode(
        ReadOnlySpan<byte> encoded,
        out Bitmap? bitmap,
        out int errorHResult)
    {
        bitmap = null;
        errorHResult = 0;

        if (encoded.IsEmpty)
        {
            errorHResult = unchecked((int)0x80070057); // E_INVALIDARG
            return false;
        }

        IWICImagingFactory? factory = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICFormatConverter? converter = null;
        IStream? stream = null;

        try
        {
            stream = CreateMemoryStream(encoded);
            factory = CreateFactory();

            var hr = factory.CreateDecoderFromStream(
                stream,
                IntPtr.Zero,
                WICDecodeOptions.MetadataCacheOnDemand,
                out decoder);
            if (hr < 0)
                return Fail(hr, out errorHResult);

            hr = decoder.GetFrame(0, out frame);
            if (hr < 0)
                return Fail(hr, out errorHResult);

            hr = factory.CreateFormatConverter(out converter);
            if (hr < 0)
                return Fail(hr, out errorHResult);

            var destinationFormat = PixelFormat32BppBgra;
            hr = converter.Initialize(
                frame,
                ref destinationFormat,
                WICBitmapDitherType.None,
                IntPtr.Zero,
                0d,
                WICBitmapPaletteType.Custom);
            if (hr < 0)
                return Fail(hr, out errorHResult);

            hr = converter.GetSize(out var width, out var height);
            if (hr < 0)
                return Fail(hr, out errorHResult);
            if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
                return Fail(unchecked((int)0x80070057), out errorHResult);

            var stride = checked((int)width * 4);
            var bufferSize = checked(stride * (int)height);
            var pixels = GC.AllocateUninitializedArray<byte>(bufferSize);

            unsafe
            {
                fixed (byte* pixelPointer = pixels)
                {
                    hr = converter.CopyPixels(
                        IntPtr.Zero,
                        checked((uint)stride),
                        checked((uint)bufferSize),
                        (IntPtr)pixelPointer);
                }
            }

            if (hr < 0)
                return Fail(hr, out errorHResult);

            var result = new Bitmap((int)width, (int)height, PixelFormat.Format32bppArgb);
            try
            {
                var bits = result.LockBits(
                    new Rectangle(0, 0, result.Width, result.Height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    for (var y = 0; y < result.Height; y++)
                    {
                        Marshal.Copy(
                            pixels,
                            y * stride,
                            IntPtr.Add(bits.Scan0, y * bits.Stride),
                            stride);
                    }
                }
                finally
                {
                    result.UnlockBits(bits);
                }
            }
            catch
            {
                result.Dispose();
                throw;
            }

            bitmap = result;
            return true;
        }
        catch (COMException exception)
        {
            errorHResult = exception.HResult;
            return false;
        }
        catch (ExternalException exception)
        {
            errorHResult = exception.HResult;
            return false;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidCastException
                or OverflowException
                or PlatformNotSupportedException)
        {
            errorHResult = exception.HResult != 0 ? exception.HResult : EFail;
            return false;
        }
        finally
        {
            ReleaseComObject(converter);
            ReleaseComObject(frame);
            ReleaseComObject(decoder);
            ReleaseComObject(factory);
            ReleaseComObject(stream);
        }
    }

    private static bool Fail(int hr, out int errorHResult)
    {
        errorHResult = hr;
        return false;
    }

    private static IWICImagingFactory CreateFactory()
    {
        var type = Type.GetTypeFromCLSID(ClsidWicImagingFactory, throwOnError: true)
            ?? throw new PlatformNotSupportedException("Windows Imaging Component is unavailable.");
        return (IWICImagingFactory)(Activator.CreateInstance(type)
            ?? throw new PlatformNotSupportedException("Windows Imaging Component factory could not be created."));
    }

    private static IStream CreateMemoryStream(ReadOnlySpan<byte> encoded)
    {
        var bytes = encoded.ToArray();
        var pointer = NativeMethods.SHCreateMemStream(bytes, checked((uint)bytes.Length));
        if (pointer == IntPtr.Zero)
            throw new ExternalException("SHCreateMemStream failed.", EFail);

        IStream stream;
        try
        {
            stream = (IStream)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            _ = Marshal.Release(pointer);
        }

        stream.Seek(0, 0, IntPtr.Zero);
        return stream;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            _ = Marshal.FinalReleaseComObject(value);
    }

    private enum WICDecodeOptions : uint
    {
        MetadataCacheOnDemand = 0,
        MetadataCacheOnLoad = 1
    }

    private enum WICBitmapDitherType : uint
    {
        None = 0
    }

    private enum WICBitmapPaletteType : uint
    {
        Custom = 0
    }

    [ComImport]
    [Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapSource
    {
        [PreserveSig]
        int GetSize(out uint width, out uint height);

        [PreserveSig]
        int GetPixelFormat(out Guid pixelFormat);

        [PreserveSig]
        int GetResolution(out double dpiX, out double dpiY);

        [PreserveSig]
        int CopyPalette(IntPtr palette);

        [PreserveSig]
        int CopyPixels(
            IntPtr rectangle,
            uint stride,
            uint bufferSize,
            IntPtr buffer);
    }

    [ComImport]
    [Guid("3b16811b-6a43-4ec9-a813-3d930c13b940")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapFrameDecode : IWICBitmapSource
    {
        // [ComImport] does not include COM base-interface methods in a derived
        // interface's vtable automatically. Redeclare all IWICBitmapSource slots
        // so the three frame-specific methods begin at the native COM slot.
        [PreserveSig]
        new int GetSize(out uint width, out uint height);

        [PreserveSig]
        new int GetPixelFormat(out Guid pixelFormat);

        [PreserveSig]
        new int GetResolution(out double dpiX, out double dpiY);

        [PreserveSig]
        new int CopyPalette(IntPtr palette);

        [PreserveSig]
        new int CopyPixels(
            IntPtr rectangle,
            uint stride,
            uint bufferSize,
            IntPtr buffer);

        [PreserveSig]
        int GetMetadataQueryReader(out IntPtr metadataQueryReader);

        [PreserveSig]
        int GetColorContexts(uint count, IntPtr colorContexts, out uint actualCount);

        [PreserveSig]
        int GetThumbnail(out IntPtr thumbnail);
    }

    [ComImport]
    [Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapDecoder
    {
        [PreserveSig]
        int QueryCapability(IStream stream, out uint capability);

        [PreserveSig]
        int Initialize(IStream stream, WICDecodeOptions cacheOptions);

        [PreserveSig]
        int GetContainerFormat(out Guid containerFormat);

        [PreserveSig]
        int GetDecoderInfo(out IntPtr decoderInfo);

        [PreserveSig]
        int CopyPalette(IntPtr palette);

        [PreserveSig]
        int GetMetadataQueryReader(out IntPtr metadataQueryReader);

        [PreserveSig]
        int GetPreview(out IntPtr bitmapSource);

        [PreserveSig]
        int GetColorContexts(uint count, IntPtr colorContexts, out uint actualCount);

        [PreserveSig]
        int GetThumbnail(out IntPtr thumbnail);

        [PreserveSig]
        int GetFrameCount(out uint count);

        [PreserveSig]
        int GetFrame(uint index, out IWICBitmapFrameDecode frame);
    }

    [ComImport]
    [Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICFormatConverter : IWICBitmapSource
    {
        // Same ComImport inheritance rule as IWICBitmapFrameDecode: the native
        // converter's Initialize method is slot 8, after the five source methods.
        [PreserveSig]
        new int GetSize(out uint width, out uint height);

        [PreserveSig]
        new int GetPixelFormat(out Guid pixelFormat);

        [PreserveSig]
        new int GetResolution(out double dpiX, out double dpiY);

        [PreserveSig]
        new int CopyPalette(IntPtr palette);

        [PreserveSig]
        new int CopyPixels(
            IntPtr rectangle,
            uint stride,
            uint bufferSize,
            IntPtr buffer);

        [PreserveSig]
        int Initialize(
            IWICBitmapSource source,
            ref Guid destinationFormat,
            WICBitmapDitherType dither,
            IntPtr palette,
            double alphaThresholdPercent,
            WICBitmapPaletteType paletteTranslate);

        [PreserveSig]
        int CanConvert(ref Guid sourceFormat, ref Guid destinationFormat, out int canConvert);
    }

    [ComImport]
    [Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICImagingFactory
    {
        [PreserveSig]
        int CreateDecoderFromFilename(
            IntPtr filename,
            IntPtr vendor,
            uint desiredAccess,
            WICDecodeOptions metadataOptions,
            out IntPtr decoder);

        [PreserveSig]
        int CreateDecoderFromStream(
            IStream stream,
            IntPtr vendor,
            WICDecodeOptions metadataOptions,
            out IWICBitmapDecoder decoder);

        [PreserveSig]
        int CreateDecoderFromFileHandle(
            nuint fileHandle,
            IntPtr vendor,
            WICDecodeOptions metadataOptions,
            out IntPtr decoder);

        [PreserveSig]
        int CreateComponentInfo(ref Guid component, out IntPtr componentInfo);

        [PreserveSig]
        int CreateDecoder(ref Guid containerFormat, IntPtr vendor, out IntPtr decoder);

        [PreserveSig]
        int CreateEncoder(ref Guid containerFormat, IntPtr vendor, out IntPtr encoder);

        [PreserveSig]
        int CreatePalette(out IntPtr palette);

        [PreserveSig]
        int CreateFormatConverter(out IWICFormatConverter converter);
    }

    private static class NativeMethods
    {
        [DllImport("shlwapi.dll")]
        public static extern IntPtr SHCreateMemStream(
            [In] byte[] initialData,
            uint initialDataLength);
    }
}
