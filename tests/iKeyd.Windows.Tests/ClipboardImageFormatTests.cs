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
}
