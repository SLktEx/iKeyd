using System.Diagnostics;
using iKeyd.Core.Clipboard;

namespace iKeyd.App;

/// <summary>
/// Keeps the modal clipboard picker off the low-level keyboard-hook thread.
/// Capture/paste actions remain synchronous because they do not run a modal UI.
/// </summary>
internal sealed class DeferredClipboardHistoryActions : IClipboardHistoryActions
{
    private readonly IClipboardHistoryActions _inner;
    private readonly SynchronizationContext _uiContext;
    private readonly Action _showPicker;
    private int _pickerPending;

    public DeferredClipboardHistoryActions(
        IClipboardHistoryActions inner,
        SynchronizationContext uiContext,
        Action showPicker)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        _showPicker = showPicker ?? throw new ArgumentNullException(nameof(showPicker));
    }

    public bool ShowPickerAndPaste()
    {
        // Key repeat can dispatch M+V more than once before V is released. Keep a
        // single modal picker request outstanding so repeated hook events do not
        // queue a train of dialogs on the UI thread.
        if (Interlocked.Exchange(ref _pickerPending, 1) != 0)
            return true;

        try
        {
            _uiContext.Post(
                static state => ((DeferredClipboardHistoryActions)state!).RunPicker(),
                this);
            return true;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _pickerPending, 0);
            Trace.TraceError("Could not dispatch clipboard history to the UI thread: {0}", exception);
            return false;
        }
    }

    public bool CaptureLatest() => _inner.CaptureLatest();

    public bool PasteCaptured() => _inner.PasteCaptured();

    private void RunPicker()
    {
        try
        {
            _showPicker();
        }
        catch (Exception exception)
        {
            // The hook already returned before this callback runs. A picker/UI
            // failure therefore stays isolated from input processing.
            Trace.TraceError("Clipboard history UI failed: {0}", exception);
        }
        finally
        {
            Volatile.Write(ref _pickerPending, 0);
        }
    }
}
