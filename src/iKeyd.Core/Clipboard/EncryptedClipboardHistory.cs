using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace iKeyd.Core.Clipboard;

public enum ClipboardPayloadKind : byte
{
    Text = 1,
    Image = 2,
    Html = 3,
    RichText = 4,
    FileList = 5,
    Binary = 255
}

/// <summary>
/// Binary-safe clipboard-history payload. The system clipboard itself remains
/// untouched; this model is only for iKeyd-owned history storage.
/// </summary>
public sealed class ClipboardPayload
{
    public ClipboardPayload(ClipboardPayloadKind kind, string contentType, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type is required.", nameof(contentType));
        ArgumentNullException.ThrowIfNull(data);

        Kind = kind;
        ContentType = contentType;
        Data = data.ToArray();
    }

    public ClipboardPayloadKind Kind { get; }
    public string ContentType { get; }
    public byte[] Data { get; }

    public static ClipboardPayload FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ClipboardPayload(ClipboardPayloadKind.Text, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
    }

    public static ClipboardPayload FromImage(byte[] data, string contentType = "image/png")
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Image payload content type must start with 'image/'.", nameof(contentType));
        return new ClipboardPayload(ClipboardPayloadKind.Image, contentType, data);
    }

    public string GetText()
    {
        if (Kind != ClipboardPayloadKind.Text)
            throw new InvalidOperationException("Clipboard payload is not text.");
        return Encoding.UTF8.GetString(Data);
    }
}

public sealed record EncryptedClipboardRecord(
    int FormatVersion,
    string CipherId,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag);

/// <summary>
/// Cipher boundary for persisted clipboard history. The persisted format stores
/// the cipher id so Rust can replace the .NET implementation without changing
/// the rest of the history pipeline.
/// </summary>
public interface IClipboardHistoryCipher
{
    string CipherId { get; }

    EncryptedClipboardRecord Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default);

    byte[] Decrypt(
        EncryptedClipboardRecord record,
        ReadOnlySpan<byte> associatedData = default);
}

/// <summary>
/// .NET implementation used until the Rust port. The Rust implementation may
/// use AEGIS-256 while keeping this interface and record envelope stable.
/// </summary>
public sealed class ChaCha20Poly1305ClipboardCipher : IClipboardHistoryCipher, IDisposable
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int CurrentFormatVersion = 1;

    private readonly byte[] _key;
    private bool _disposed;

    public ChaCha20Poly1305ClipboardCipher(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be exactly {KeySize} bytes.", nameof(key));
        _key = key.ToArray();
    }

    public string CipherId => "chacha20-poly1305";

    public static ChaCha20Poly1305ClipboardCipher CreateRandom()
        => new(RandomNumberGenerator.GetBytes(KeySize));

    public EncryptedClipboardRecord Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var cipher = new ChaCha20Poly1305(_key);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedClipboardRecord(
            CurrentFormatVersion,
            CipherId,
            nonce,
            ciphertext,
            tag);
    }

    public byte[] Decrypt(
        EncryptedClipboardRecord record,
        ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(record);

        if (record.FormatVersion != CurrentFormatVersion)
            throw new NotSupportedException($"Unsupported clipboard history format version: {record.FormatVersion}.");
        if (!string.Equals(record.CipherId, CipherId, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported clipboard history cipher: {record.CipherId}.");
        if (record.Nonce.Length != NonceSize)
            throw new CryptographicException("Invalid clipboard history nonce length.");
        if (record.Tag.Length != TagSize)
            throw new CryptographicException("Invalid clipboard history authentication tag length.");

        var plaintext = new byte[record.Ciphertext.Length];
        using var cipher = new ChaCha20Poly1305(_key);
        cipher.Decrypt(record.Nonce, record.Ciphertext, record.Tag, plaintext, associatedData);
        return plaintext;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
    }
}

public static class ClipboardPayloadCodec
{
    private const byte CurrentVersion = 1;
    private const int HeaderSize = 8;

    public static byte[] Serialize(ClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var contentType = Encoding.UTF8.GetBytes(payload.ContentType);
        if (contentType.Length > ushort.MaxValue)
            throw new ArgumentException("Clipboard content type is too long.", nameof(payload));

        var result = new byte[checked(HeaderSize + contentType.Length + payload.Data.Length)];
        result[0] = CurrentVersion;
        result[1] = (byte)payload.Kind;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2, 2), (ushort)contentType.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), payload.Data.Length);
        contentType.CopyTo(result.AsSpan(HeaderSize));
        payload.Data.CopyTo(result.AsSpan(HeaderSize + contentType.Length));
        return result;
    }

    public static ClipboardPayload Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new FormatException("Clipboard payload is truncated.");
        if (bytes[0] != CurrentVersion)
            throw new NotSupportedException($"Unsupported clipboard payload version: {bytes[0]}.");

        var kind = (ClipboardPayloadKind)bytes[1];
        var contentTypeLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2));
        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4));
        if (dataLength < 0)
            throw new FormatException("Clipboard payload contains a negative data length.");

        var expectedLength = checked(HeaderSize + contentTypeLength + dataLength);
        if (bytes.Length != expectedLength)
            throw new FormatException("Clipboard payload length does not match its header.");

        var contentType = Encoding.UTF8.GetString(bytes.Slice(HeaderSize, contentTypeLength));
        var data = bytes.Slice(HeaderSize + contentTypeLength, dataLength).ToArray();
        return new ClipboardPayload(kind, contentType, data);
    }
}

public static class ClipboardHistoryEncryption
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("iKeyd.clipboard-history.payload/v1");

    public static EncryptedClipboardRecord Encrypt(
        ClipboardPayload payload,
        IClipboardHistoryCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(cipher);
        return cipher.Encrypt(ClipboardPayloadCodec.Serialize(payload), Domain);
    }

    public static ClipboardPayload Decrypt(
        EncryptedClipboardRecord record,
        IClipboardHistoryCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(cipher);
        return ClipboardPayloadCodec.Deserialize(cipher.Decrypt(record, Domain));
    }
}
