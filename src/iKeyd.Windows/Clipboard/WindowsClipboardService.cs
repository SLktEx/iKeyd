using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using iKeyd.Core.Clipboard;

namespace iKeyd.Windows.Clipboard;

public sealed class WindowsClipboardService : IClipboardService, IClipboardPayloadService
{
    private static readonly IReadOnlyDictionary<string, string> NativeImageContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PNG"] = "image/png",
            ["image/png"] = "image/png",
            ["JFIF"] = "image/jpeg",
            ["JPEG"] = "image/jpeg",
            ["JPG"] = "image/jpeg",
            ["image/jpeg"] = "image/jpeg",
            ["image/jpg"] = "image/jpeg",
            ["GIF"] = "image/gif",
            ["image/gif"] = "image/gif",
            ["TIFF"] = "image/tiff",
            ["image/tiff"] = "image/tiff",
            ["BMP"] = "image/bmp",
            ["image/bmp"] = "image/bmp",
            ["image/x-ms-bmp"] = "image/bmp"
        };

    private static readonly IReadOnlyDictionary<string, string> PreferredNativeImageFormats =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = "PNG",
            ["image/jpeg"] = "JFIF",
            ["image/gif"] = "GIF",
            ["image/tiff"] = "TIFF",
            ["image/bmp"] = "BMP"
        };

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private ClipboardListenerControl? _listener;
    private Exception? _startupException;
    private bool _disposed;

    public WindowsClipboardService()
    {
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "iKeyd Clipboard Listener"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();

        if (_startupException is not null)
            throw new InvalidOperationException("Failed to start the Windows clipboard listener.", _startupException);
    }

    public event EventHandler? Changed;

    public string? ReadText()
        => InvokeOnClipboardThread(() => RetryClipboard(() =>
            System.Windows.Forms.Clipboard.ContainsText(TextDataFormat.UnicodeText)
                ? System.Windows.Forms.Clipboard.GetText(TextDataFormat.UnicodeText)
                : null));

    public ClipboardPayload? ReadPayload()
        => InvokeOnClipboardThread(() => RetryClipboard(() =>
        {
            var encodedImage = TryReadEncodedImage(System.Windows.Forms.Clipboard.GetDataObject());
            if (encodedImage is not null)
                return encodedImage;

            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                using var image = System.Windows.Forms.Clipboard.GetImage();
                if (image is not null)
                {
                    // Some applications expose only a decoded bitmap/DIB. Keep the
                    // PNG conversion as the compatibility fallback, while preserving
                    // native PNG/JPEG/GIF/TIFF/BMP bytes when they are exposed.
                    using var stream = new MemoryStream();
                    image.Save(stream, ImageFormat.Png);
                    return ClipboardPayload.FromImage(stream.ToArray(), "image/png");
                }
            }

            if (System.Windows.Forms.Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                var text = System.Windows.Forms.Clipboard.GetText(TextDataFormat.UnicodeText);
                return string.IsNullOrEmpty(text) ? null : ClipboardPayload.FromText(text);
            }

            return null;
        }));

    internal static ClipboardPayload? TryReadEncodedImage(System.Windows.Forms.IDataObject? dataObject)
    {
        if (dataObject is null)
            return null;

        foreach (var format in dataObject.GetFormats(autoConvert: false))
        {
            if (!NativeImageContentTypes.TryGetValue(format, out var contentType))
                continue;

            object? value;
            try
            {
                value = dataObject.GetData(format, autoConvert: false);
            }
            catch (ExternalException)
            {
                // One malformed/unavailable native format must not prevent the
                // bitmap fallback or other clipboard formats from being captured.
                continue;
            }

            var bytes = TryReadBytes(value);
            if (bytes is { Length: > 0 })
                return ClipboardPayload.FromImage(bytes, contentType);
        }

        return null;
    }

    internal static ClipboardImageRestoreData PrepareImageRestore(ClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Kind != ClipboardPayloadKind.Image)
            throw new ArgumentException("Clipboard payload must be an image.", nameof(payload));
        if (payload.Data.Length == 0)
            throw new InvalidDataException("Clipboard image payload is empty.");

        Bitmap bitmap;
        try
        {
            using var sourceStream = new MemoryStream(payload.Data, writable: false);
            using var source = Image.FromStream(
                sourceStream,
                useEmbeddedColorManagement: true,
                validateImageData: true);
            bitmap = new Bitmap(source);
        }
        catch (Exception exception) when (
            exception is ArgumentException or OutOfMemoryException or ExternalException)
        {
            throw new InvalidDataException(
                $"Clipboard image payload '{payload.ContentType}' cannot be decoded by the Windows image backend.",
                exception);
        }

        var dataObject = new DataObject();
        var ownedStreams = new List<MemoryStream>();
        try
        {
            // Publish a normal Bitmap/DIB representation for applications that do
            // not understand the encoded source format.
            dataObject.SetData(DataFormats.Bitmap, autoConvert: true, bitmap);

            // Also put the original encoded bytes back under the common Windows
            // registered format so format-aware applications can retain JPEG/GIF/
            // TIFF/PNG/BMP data (including animation/pages where applicable).
            var contentType = NormalizeImageContentType(payload.ContentType);
            if (PreferredNativeImageFormats.TryGetValue(contentType, out var nativeFormat))
            {
                var encodedStream = new MemoryStream(payload.Data, writable: false);
                ownedStreams.Add(encodedStream);
                dataObject.SetData(nativeFormat, autoConvert: false, encodedStream);
            }

            return new ClipboardImageRestoreData(dataObject, bitmap, ownedStreams);
        }
        catch
        {
            foreach (var stream in ownedStreams)
                stream.Dispose();
            bitmap.Dispose();
            throw;
        }
    }

    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        InvokeOnClipboardThread(() =>
        {
            RetryClipboard(() =>
            {
                if (text.Length == 0)
                    System.Windows.Forms.Clipboard.Clear();
                else
                    System.Windows.Forms.Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            });
            return true;
        });
    }

    public void WritePayload(ClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        InvokeOnClipboardThread(() =>
        {
            RetryClipboard(() =>
            {
                switch (payload.Kind)
                {
                    case ClipboardPayloadKind.Text:
                        var text = payload.GetText();
                        if (text.Length == 0)
                            System.Windows.Forms.Clipboard.Clear();
                        else
                            System.Windows.Forms.Clipboard.SetText(text, TextDataFormat.UnicodeText);
                        break;

                    case ClipboardPayloadKind.Image:
                        using (var restoreData = PrepareImageRestore(payload))
                        {
                            // copy=true flushes the data into the system clipboard
                            // before the backing bitmap/streams are disposed.
                            System.Windows.Forms.Clipboard.SetDataObject(restoreData.DataObject, copy: true);
                        }
                        break;

                    default:
                        throw new NotSupportedException(
                            $"Clipboard payload kind '{payload.Kind}' cannot be restored on Windows yet.");
                }

                return true;
            });
            return true;
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var listener = _listener;
        if (listener is not null && listener.IsHandleCreated)
        {
            try
            {
                listener.BeginInvoke(new Action(Application.ExitThread));
            }
            catch (InvalidOperationException)
            {
                // The listener thread has already exited.
            }
        }

        if (Thread.CurrentThread != _thread)
            _thread.Join();
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        try
        {
            using var listener = new ClipboardListenerControl(OnClipboardChanged);
            _listener = listener;
            _ = listener.Handle;
            _ready.Set();
            Application.Run();
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _ready.Set();
        }
        finally
        {
            _listener = null;
        }
    }

    private void OnClipboardChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    private T InvokeOnClipboardThread<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var listener = _listener ?? throw new InvalidOperationException("Clipboard listener is not running.");
        if (!listener.InvokeRequired)
            return action();
        return (T)listener.Invoke(action);
    }

    private static T RetryClipboard<T>(Func<T> action)
    {
        const int attempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (ExternalException) when (attempt < attempts)
            {
                Thread.Sleep(25);
            }
        }
    }

    private static string NormalizeImageContentType(string contentType)
    {
        var separator = contentType.IndexOf(';');
        var mediaType = separator >= 0 ? contentType[..separator] : contentType;
        return mediaType.Trim().ToLowerInvariant() switch
        {
            "image/jpg" => "image/jpeg",
            "image/x-ms-bmp" => "image/bmp",
            var normalized => normalized
        };
    }

    private static byte[]? TryReadBytes(object? value)
    {
        if (value is byte[] bytes)
            return bytes.ToArray();
        if (value is not Stream stream)
            return null;

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

    private sealed class ClipboardListenerControl : Control
    {
        private const int WmClipboardUpdate = 0x031D;
        private readonly Action _changed;

        public ClipboardListenerControl(Action changed)
            => _changed = changed;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!NativeMethods.AddClipboardFormatListener(Handle))
                throw new InvalidOperationException("AddClipboardFormatListener failed.");
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (Handle != IntPtr.Zero)
                NativeMethods.RemoveClipboardFormatListener(Handle);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmClipboardUpdate)
                _changed();
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(nint window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(nint window);
    }
}

internal sealed class ClipboardImageRestoreData : IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly IReadOnlyList<MemoryStream> _streams;
    private bool _disposed;

    public ClipboardImageRestoreData(
        DataObject dataObject,
        Bitmap bitmap,
        IReadOnlyList<MemoryStream> streams)
    {
        DataObject = dataObject ?? throw new ArgumentNullException(nameof(dataObject));
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        _streams = streams ?? throw new ArgumentNullException(nameof(streams));
    }

    public DataObject DataObject { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var stream in _streams)
            stream.Dispose();
        _bitmap.Dispose();
    }
}
