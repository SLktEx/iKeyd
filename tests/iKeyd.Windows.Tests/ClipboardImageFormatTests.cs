using System.Drawing;
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

    [Theory]
    [InlineData("image/png", "PNG", "image/png")]
    [InlineData("image/jpeg", "JFIF", "image/jpeg")]
    [InlineData("image/gif", "GIF", "image/gif")]
    [InlineData("image/tiff", "TIFF", "image/tiff")]
    [InlineData("image/bmp", "BMP", "image/bmp")]
    public void Restore_data_object_keeps_encoded_bytes_and_standard_bitmap(
        string contentType,
        string nativeFormat,
        string mimeFormat)
    {
        var bytes = CreateEncodedImage(contentType);
        var payload = ClipboardPayload.FromImage(bytes, contentType);

        using var bitmap = WindowsClipboardService.DecodeImageForRestore(payload);
        var dataObject = WindowsClipboardService.CreateImageRestoreDataObject(payload, bitmap);

        Assert.Equal(2, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
        Assert.True(dataObject.GetDataPresent(DataFormats.Bitmap, autoConvert: false));
        Assert.Same(bitmap, dataObject.GetData(DataFormats.Bitmap, autoConvert: false));
        Assert.Equal(bytes, Assert.IsType<byte[]>(dataObject.GetData(nativeFormat, autoConvert: false)));
        Assert.Equal(bytes, Assert.IsType<byte[]>(dataObject.GetData(mimeFormat, autoConvert: false)));
    }

    [Fact]
    public void Bmp_restore_publishes_all_capture_aliases()
    {
        var bytes = CreateEncodedImage("image/bmp");
        var payload = ClipboardPayload.FromImage(bytes, "image/bmp");

        using var bitmap = WindowsClipboardService.DecodeImageForRestore(payload);
        var dataObject = WindowsClipboardService.CreateImageRestoreDataObject(payload, bitmap);

        foreach (var format in new[] { "BMP", "image/bmp", "image/x-ms-bmp" })
            Assert.Equal(bytes, Assert.IsType<byte[]>(dataObject.GetData(format, autoConvert: false)));
    }

    [Fact]
    public void Malformed_known_image_fails_before_clipboard_restore()
    {
        var payload = ClipboardPayload.FromImage([0x00, 0x01, 0x02, 0x03], "image/jpeg");

        var error = Assert.Throws<InvalidDataException>(() => WindowsClipboardService.DecodeImageForRestore(payload));

        Assert.Contains("image/jpeg", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_image_type_is_not_claimed_by_v07_restore()
    {
        var payload = ClipboardPayload.FromImage([0x52, 0x49, 0x46, 0x46], "image/webp");

        Assert.Throws<NotSupportedException>(() => WindowsClipboardService.DecodeImageForRestore(payload));
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

    private static byte[] CreateEncodedImage(string contentType)
    {
        using var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(1, 0, Color.Green);
        bitmap.SetPixel(0, 1, Color.Blue);
        bitmap.SetPixel(1, 1, Color.White);

        var format = contentType switch
        {
            "image/png" => ImageFormat.Png,
            "image/jpeg" => ImageFormat.Jpeg,
            "image/gif" => ImageFormat.Gif,
            "image/tiff" => ImageFormat.Tiff,
            "image/bmp" => ImageFormat.Bmp,
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Unsupported test image type.")
        };

        using var stream = new MemoryStream();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }
}
