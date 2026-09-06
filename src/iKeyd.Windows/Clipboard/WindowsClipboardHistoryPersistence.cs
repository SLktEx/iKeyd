using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using iKeyd.Core.Clipboard;

namespace iKeyd.Windows.Clipboard;

/// <summary>
/// Persists only encrypted iKeyd clipboard-history records. The normal Windows
/// clipboard remains untouched. The ChaCha20-Poly1305 master key is protected
/// with Windows DPAPI for the current user.
/// </summary>
public sealed class WindowsClipboardHistoryPersistence : IClipboardHistoryPersistence, IDisposable
{
    private const string KeyFileName = "clipboard-history.key";
    private const string HistoryFileName = "clipboard-history.json";
    private static readonly byte[] DpapiEntropy = "iKeyd.clipboard-history.key/v1"u8.ToArray();

    private readonly string _historyPath;
    private readonly string _keyPath;
    private readonly ChaCha20Poly1305ClipboardCipher _cipher;
    private bool _disposed;

    public WindowsClipboardHistoryPersistence(string? directory = null)
    {
        var dataDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iKeyd");
        Directory.CreateDirectory(dataDirectory);

        _historyPath = Path.Combine(dataDirectory, HistoryFileName);
        _keyPath = Path.Combine(dataDirectory, KeyFileName);

        var key = LoadOrCreateMasterKey();
        try
        {
            _cipher = new ChaCha20Poly1305ClipboardCipher(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public IReadOnlyList<ClipboardPayload> Load()
    {
        ThrowIfDisposed();
        if (!File.Exists(_historyPath))
            return Array.Empty<ClipboardPayload>();

        PersistedClipboardHistory? persisted;
        try
        {
            persisted = JsonSerializer.Deserialize<PersistedClipboardHistory>(File.ReadAllText(_historyPath));
        }
        catch (JsonException)
        {
            return Array.Empty<ClipboardPayload>();
        }

        if (persisted is null || persisted.FormatVersion != 1)
            return Array.Empty<ClipboardPayload>();

        var items = new List<ClipboardPayload>(persisted.Items.Count);
        foreach (var item in persisted.Items)
        {
            try
            {
                var record = new EncryptedClipboardRecord(
                    item.FormatVersion,
                    item.CipherId,
                    Convert.FromBase64String(item.Nonce),
                    Convert.FromBase64String(item.Ciphertext),
                    Convert.FromBase64String(item.Tag));
                items.Add(ClipboardHistoryEncryption.Decrypt(record, _cipher));
            }
            catch (Exception exception) when (
                exception is FormatException or CryptographicException or NotSupportedException)
            {
                // One corrupt item must not make all remaining history unavailable.
            }
        }

        return items;
    }

    public void Save(IReadOnlyList<ClipboardPayload> items)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);

        var persisted = new PersistedClipboardHistory
        {
            FormatVersion = 1,
            Items = items.Select(item =>
            {
                var encrypted = ClipboardHistoryEncryption.Encrypt(item, _cipher);
                return new PersistedClipboardRecord
                {
                    FormatVersion = encrypted.FormatVersion,
                    CipherId = encrypted.CipherId,
                    Nonce = Convert.ToBase64String(encrypted.Nonce),
                    Ciphertext = Convert.ToBase64String(encrypted.Ciphertext),
                    Tag = Convert.ToBase64String(encrypted.Tag)
                };
            }).ToList()
        };

        var tempPath = _historyPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(persisted));
        File.Move(tempPath, _historyPath, true);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        if (File.Exists(_historyPath))
            File.Delete(_historyPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cipher.Dispose();
    }

    private byte[] LoadOrCreateMasterKey()
    {
        if (File.Exists(_keyPath))
            return Dpapi.Unprotect(File.ReadAllBytes(_keyPath), DpapiEntropy);

        var key = RandomNumberGenerator.GetBytes(ChaCha20Poly1305ClipboardCipher.KeySize);
        try
        {
            var protectedKey = Dpapi.Protect(key, DpapiEntropy);
            File.WriteAllBytes(_keyPath, protectedKey);
            return key;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class PersistedClipboardHistory
    {
        public int FormatVersion { get; set; }
        public List<PersistedClipboardRecord> Items { get; set; } = [];
    }

    private sealed class PersistedClipboardRecord
    {
        public int FormatVersion { get; set; }
        public string CipherId { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
    }

    private static class Dpapi
    {
        private const int CryptProtectUiForbidden = 0x1;

        public static byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
            => Transform(plaintext, entropy, protect: true);

        public static byte[] Unprotect(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> entropy)
            => Transform(ciphertext, entropy, protect: false);

        private static byte[] Transform(ReadOnlySpan<byte> input, ReadOnlySpan<byte> entropy, bool protect)
        {
            var inputBlob = Allocate(input);
            var entropyBlob = Allocate(entropy);
            DataBlob outputBlob = default;
            try
            {
                var success = protect
                    ? CryptProtectData(
                        ref inputBlob,
                        null,
                        ref entropyBlob,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out outputBlob)
                    : CryptUnprotectData(
                        ref inputBlob,
                        IntPtr.Zero,
                        ref entropyBlob,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out outputBlob);

                if (!success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var result = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Size);
                return result;
            }
            finally
            {
                FreeAllocated(ref inputBlob);
                FreeAllocated(ref entropyBlob);
                if (outputBlob.Data != IntPtr.Zero)
                    LocalFree(outputBlob.Data);
            }
        }

        private static DataBlob Allocate(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                return default;

            var copy = data.ToArray();
            var pointer = Marshal.AllocHGlobal(copy.Length);
            try
            {
                Marshal.Copy(copy, 0, pointer, copy.Length);
                return new DataBlob { Size = copy.Length, Data = pointer };
            }
            catch
            {
                Marshal.FreeHGlobal(pointer);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(copy);
            }
        }

        private static void FreeAllocated(ref DataBlob blob)
        {
            if (blob.Data == IntPtr.Zero)
                return;
            Marshal.FreeHGlobal(blob.Data);
            blob = default;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? dataDescription,
            ref DataBlob optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr dataDescription,
            ref DataBlob optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
