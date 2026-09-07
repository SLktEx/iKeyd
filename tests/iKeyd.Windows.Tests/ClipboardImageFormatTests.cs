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

        Assert.True(restore.DataObject.GetDataPresent(DataFormats.Bitmap, autoConvert: false));
        var bitmap = Assert.IsType<Bitmap>(
            restore.DataObject.GetData(DataFormats.Bitmap, autoConvert: false));
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(2, bitmap.Height);

        Assert.True(restore.DataObject.GetDataPresent(expectedNativeFormat, autoConvert: false));
        var native = restore.DataObject.GetData(expectedNativeFormat, autoConvert: false);
        Assert.Equal(bytes, ReadBytes(native));
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
    public void Malformed_image_payload_reports_controlled_restore_failure()
    {
        var payload = ClipboardPayload.FromImage([0x01, 0x02, 0x03, 0x04], "image/png");

        var exception = Assert.Throws<InvalidDataException>(
            () => WindowsClipboardService.PrepareImageRestore(payload));

        Assert.Contains("image/png", exception.Message, StringComparison.Ordinal);
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
