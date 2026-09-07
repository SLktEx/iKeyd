using System.Drawing.Imaging;
using iKeyd.Core.Clipboard;
using iKeyd.Windows.Clipboard;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ClipboardImageFormatTests
{
    [Theory]
    [InlineData("JFIF", "image/jpeg")]
    [InlineData("JPEG", "image/jpeg")]
    [InlineData("JPG", "image/jpeg")]
    [InlineData("image/jpeg", "image/jpeg")]
    [InlineData("PNG", "image/png")]
    [InlineData("image/png", "image/png")]
    [InlineData("GIF", "image/gif")]
    [InlineData("image/gif", "image/gif")]
    [InlineData("TIFF", "image/tiff")]
    [InlineData("image/tiff", "image/tiff")]
    [InlineData("BMP", "image/bmp")]
    [InlineData("image/bmp", "image/bmp")]
    [InlineData("image/x-ms-bmp", "image/bmp")]
    [InlineData("WEBP", "image/webp")]
    [InlineData("WebP", "image/webp")]
    [InlineData("image/webp", "image/webp")]
    [InlineData("HEIC", "image/heic")]
    [InlineData("image/heic", "image/heic")]
    [InlineData("HEIF", "image/heif")]
    [InlineData("image/heif", "image/heif")]
    [InlineData("AVIF", "image/avif")]
    [InlineData("image/avif", "image/avif")]
    [InlineData("ICO", "image/ico")]
    [InlineData("image/x-icon", "image/ico")]
    [InlineData("image/vnd.microsoft.icon", "image/ico")]
    [InlineData("JPEG XR", "image/jxr")]
    [InlineData("JXR", "image/jxr")]
    [InlineData("image/jxr", "image/jxr")]
    [InlineData("WMP", "image/vnd.ms-photo")]
    [InlineData("image/vnd.ms-photo", "image/vnd.ms-photo")]
    [InlineData("DDS", "image/vnd.ms-dds")]
    [InlineData("image/vnd.ms-dds", "image/vnd.ms-dds")]
    public void Native_encoded_image_bytes_are_preserved(string format, string expectedContentType)
    {
        var bytes = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89 };
        var dataObject = new DataObject();
        dataObject.SetData(format, autoConvert: false, bytes);

        var payload = WindowsClipboardService.TryReadEncodedImage(dataObject);

        Assert.NotNull(payload);
        Assert.Equal(ClipboardPayloadKind.Image, payload.Kind);
        Assert.Equal(expectedContentType, payload.ContentType);
        Assert.Equal(bytes, payload.Data);
    }

    [Theory]
    [InlineData("image/png", "PNG")]
    [InlineData("image/jpeg", "JFIF")]
    [InlineData("image/jpg", "JFIF")]
    [InlineData("image/gif", "GIF")]
    [InlineData("image/tiff", "TIFF")]
    [InlineData("image/bmp", "BMP")]
    [InlineData("image/x-ms-bmp", "BMP")]
    public void Common_encoded_images_prepare_bitmap_and_native_restore(
        string contentType,
        string expectedNativeFormat)
    {
        var bytes = CreateImageBytes(contentType);
        var payload = ClipboardPayload.FromImage(bytes, contentType);

        using var restore = WindowsClipboardService.PrepareImageRestore(payload);

        Assert.True(restore.HasBitmap);
        Assert.Null(restore.DecodeErrorHResult);
        Assert.True(restore.DataObject.GetDataPresent(DataFormats.Bitmap, autoConvert: false));
        var bitmap = Assert.IsType<Bitmap>(
            restore.DataObject.GetData(DataFormats.Bitmap, autoConvert: false));
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(2, bitmap.Height);

        Assert.True(restore.DataObject.GetDataPresent(expectedNativeFormat, autoConvert: false));
        Assert.Equal(bytes, ReadBytes(
            restore.DataObject.GetData(expectedNativeFormat, autoConvert: false)));

        var normalizedMime = contentType.ToLowerInvariant() switch
        {
            "image/jpg" => "image/jpeg",
            "image/x-ms-bmp" => "image/bmp",
            var value => value
        };
        Assert.True(restore.DataObject.GetDataPresent(normalizedMime, autoConvert: false));
        Assert.Equal(bytes, ReadBytes(
            restore.DataObject.GetData(normalizedMime, autoConvert: false)));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/tiff")]
    [InlineData("image/bmp")]
    public void Wic_decodes_builtin_common_formats(string contentType)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var bytes = CreateImageBytes(contentType);
        var decoded = WindowsWicImageDecoder.TryDecode(bytes, out var bitmap, out var error);

        Assert.True(decoded, $"WIC decode failed for {contentType} with HRESULT 0x{error:X8}.");
        using (bitmap)
        {
            Assert.NotNull(bitmap);
            Assert.Equal(2, bitmap.Width);
            Assert.Equal(2, bitmap.Height);
        }
    }

    [Theory]
    [InlineData("image/webp", "WEBP")]
    [InlineData("image/heic", "HEIC")]
    [InlineData("image/heif", "HEIF")]
    [InlineData("image/avif", "AVIF")]
    [InlineData("image/x-icon", "ICO")]
    [InlineData("image/jxr", "JPEG XR")]
    [InlineData("image/vnd.ms-photo", "WMP")]
    [InlineData("image/vnd.ms-dds", "DDS")]
    public void Codec_optional_formats_keep_encoded_data_even_when_bitmap_decode_is_unavailable(
        string contentType,
        string expectedNativeFormat)
    {
        var bytes = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };
        var payload = ClipboardPayload.FromImage(bytes, contentType);

        using var restore = WindowsClipboardService.PrepareImageRestore(payload);

        Assert.True(restore.DataObject.GetDataPresent(expectedNativeFormat, autoConvert: false));
        Assert.Equal(bytes, ReadBytes(
            restore.DataObject.GetData(expectedNativeFormat, autoConvert: false)));

        var normalizedMime = contentType == "image/x-icon" ? "image/ico" : contentType;
        Assert.True(restore.DataObject.GetDataPresent(normalizedMime, autoConvert: false));
        Assert.Equal(bytes, ReadBytes(
            restore.DataObject.GetData(normalizedMime, autoConvert: false)));

        if (!restore.HasBitmap)
            Assert.NotNull(restore.DecodeErrorHResult);
    }

    [Fact]
    public void Webp_uses_installed_WIC_codec_when_available()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var bytes = Convert.FromBase64String(
            "UklGRhwAAABXRUJQVlA4TA8AAAAvAUAAAAcQ/Y/+ByKi/wEA");

        var decoded = WindowsWicImageDecoder.TryDecode(bytes, out var bitmap, out _);
        if (!decoded)
            return; // WebP Image Extension is not guaranteed to be installed.

        using (bitmap)
        {
            Assert.NotNull(bitmap);
            Assert.Equal(2, bitmap.Width);
            Assert.Equal(2, bitmap.Height);
        }
    }

    [Fact]
    public void Avif_uses_installed_WIC_codec_when_available()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var bytes = Convert.FromBase64String(
            "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUIAAADrbWV0YQAAAAAAAAAhaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAAAAAAAOcGl0bQAAAAAAAQAAAB5pbG9jAAAAAEQAAAEAAQAAAAEAAAETAAAAKgAAAChpaW5mAAAAAAABAAAAGmluZmUCAAAAAAEAAGF2MDFDb2xvcgAAAABqaXBycAAAAEtpcGNvAAAAFGlzcGUAAAAAAAAAAgAAAAIAAAAQcGl4aQAAAAADCAgIAAAADGF2MUOBAAwAAAAAE2NvbHJuY2x4AAEADQAGgAAAABdpcG1hAAAAAAAAAAEAAQQBAoMEAAAAMm1kYXQSAAoIGAA2iAhoNCAyHBTHh4ZlAgggnlAAAABIWtlc1jIgMQsbXgqRN4A=");

        var decoded = WindowsWicImageDecoder.TryDecode(bytes, out var bitmap, out _);
        if (!decoded)
            return; // AV1/AVIF codec availability depends on the Windows install.

        using (bitmap)
        {
            Assert.NotNull(bitmap);
            Assert.Equal(2, bitmap.Width);
            Assert.Equal(2, bitmap.Height);
        }
    }

    [Fact]
    public void Stream_backed_native_image_is_read_from_start_and_position_is_restored()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x10, 0x20 };
        using var stream = new MemoryStream(bytes);
        stream.Position = 3;
        var dataObject = new DataObject();
        dataObject.SetData("JFIF", autoConvert: false, stream);

        var payload = WindowsClipboardService.TryReadEncodedImage(dataObject);

        Assert.NotNull(payload);
        Assert.Equal("image/jpeg", payload.ContentType);
        Assert.Equal(bytes, payload.Data);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Malformed_image_payload_is_preserved_without_crashing_restore()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var payload = ClipboardPayload.FromImage(bytes, "image/png");

        using var restore = WindowsClipboardService.PrepareImageRestore(payload);

        Assert.False(restore.HasBitmap);
        Assert.NotNull(restore.DecodeErrorHResult);
        Assert.True(restore.DataObject.GetDataPresent("PNG", autoConvert: false));
        Assert.Equal(bytes, ReadBytes(restore.DataObject.GetData("PNG", autoConvert: false)));
        Assert.True(restore.DataObject.GetDataPresent("image/png", autoConvert: false));
        Assert.Equal(bytes, ReadBytes(restore.DataObject.GetData("image/png", autoConvert: false)));
    }

    [Fact]
    public void Unknown_non_image_format_is_ignored()
    {
        var dataObject = new DataObject();
        dataObject.SetData("application/x-ikeyd-test", autoConvert: false, new byte[] { 1, 2, 3 });

        Assert.Null(WindowsClipboardService.TryReadEncodedImage(dataObject));
    }

    [Fact]
    public void Empty_native_image_payload_is_ignored()
    {
        var dataObject = new DataObject();
        dataObject.SetData("JPEG", autoConvert: false, Array.Empty<byte>());

        Assert.Null(WindowsClipboardService.TryReadEncodedImage(dataObject));
    }

    private static byte[] CreateImageBytes(string contentType)
    {
        var normalized = contentType.ToLowerInvariant() switch
        {
            "image/jpg" => "image/jpeg",
            "image/x-ms-bmp" => "image/bmp",
            var value => value
        };

        var format = normalized switch
        {
            "image/png" => ImageFormat.Png,
            "image/jpeg" => ImageFormat.Jpeg,
            "image/gif" => ImageFormat.Gif,
            "image/tiff" => ImageFormat.Tiff,
            "image/bmp" => ImageFormat.Bmp,
            _ => throw new ArgumentOutOfRangeException(nameof(contentType))
        };

        using var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(1, 0, Color.Green);
        bitmap.SetPixel(0, 1, Color.Blue);
        bitmap.SetPixel(1, 1, Color.White);
        using var stream = new MemoryStream();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }

    private static byte[] ReadBytes(object? value)
    {
        if (value is byte[] bytes)
            return bytes;

        var stream = Assert.IsAssignableFrom<Stream>(value);
        long? originalPosition = null;
        if (stream.CanSeek)
        {
            originalPosition = stream.Position;
            stream.Position = 0;
        }

        try
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        finally
        {
            if (originalPosition is not null)
                stream.Position = originalPosition.Value;
        }
    }
}
