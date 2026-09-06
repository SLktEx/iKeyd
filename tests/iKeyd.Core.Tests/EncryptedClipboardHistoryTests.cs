using System.Security.Cryptography;
using iKeyd.Core.Clipboard;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class EncryptedClipboardHistoryTests
{
    [Fact]
    public void Text_payload_round_trips_through_encryption()
    {
        using var cipher = ChaCha20Poly1305ClipboardCipher.CreateRandom();
        var original = ClipboardPayload.FromText("hello clipboard");

        var encrypted = ClipboardHistoryEncryption.Encrypt(original, cipher);
        var restored = ClipboardHistoryEncryption.Decrypt(encrypted, cipher);

        Assert.Equal(ClipboardPayloadKind.Text, restored.Kind);
        Assert.Equal("text/plain; charset=utf-8", restored.ContentType);
        Assert.Equal("hello clipboard", restored.GetText());
        Assert.NotEqual(original.Data, encrypted.Ciphertext);
    }

    [Fact]
    public void Image_payload_round_trips_without_text_conversion()
    {
        using var cipher = ChaCha20Poly1305ClipboardCipher.CreateRandom();
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        };
        var original = ClipboardPayload.FromImage(pngBytes, "image/png");

        var encrypted = ClipboardHistoryEncryption.Encrypt(original, cipher);
        var restored = ClipboardHistoryEncryption.Decrypt(encrypted, cipher);

        Assert.Equal(ClipboardPayloadKind.Image, restored.Kind);
        Assert.Equal("image/png", restored.ContentType);
        Assert.Equal(pngBytes, restored.Data);
    }

    [Fact]
    public void Ciphertext_tampering_is_detected()
    {
        using var cipher = ChaCha20Poly1305ClipboardCipher.CreateRandom();
        var encrypted = ClipboardHistoryEncryption.Encrypt(ClipboardPayload.FromText("secret"), cipher);
        encrypted.Ciphertext[0] ^= 0x80;

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            ClipboardHistoryEncryption.Decrypt(encrypted, cipher));
    }

    [Fact]
    public void Payload_codec_supports_binary_data_with_zero_bytes()
    {
        var original = new ClipboardPayload(
            ClipboardPayloadKind.Binary,
            "application/octet-stream",
            new byte[] { 0x00, 0x01, 0x00, 0xFF });

        var serialized = ClipboardPayloadCodec.Serialize(original);
        var restored = ClipboardPayloadCodec.Deserialize(serialized);

        Assert.Equal(original.Kind, restored.Kind);
        Assert.Equal(original.ContentType, restored.ContentType);
        Assert.Equal(original.Data, restored.Data);
    }

    [Fact]
    public void Wrong_key_cannot_decrypt_history()
    {
        using var writer = ChaCha20Poly1305ClipboardCipher.CreateRandom();
        using var reader = ChaCha20Poly1305ClipboardCipher.CreateRandom();
        var encrypted = ClipboardHistoryEncryption.Encrypt(ClipboardPayload.FromText("secret"), writer);

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            ClipboardHistoryEncryption.Decrypt(encrypted, reader));
    }
}
