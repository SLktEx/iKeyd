using System.Runtime.InteropServices;
using iKeyd.Core.Clipboard;

namespace iKeyd.Windows.Clipboard;

public sealed class WindowsClipboardService : IClipboardService
{
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
