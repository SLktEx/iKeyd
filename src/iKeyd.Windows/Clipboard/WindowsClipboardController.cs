using iKeyd.Core.Clipboard;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Clipboard;

public sealed class WindowsClipboardController : IClipboardHistoryActions, IDisposable
{
    private const ushort VkShift = 0x10;
    private const ushort VkInsert = 0x2D;

    private readonly IClipboardService _clipboard;
    private readonly IClipboardPayloadService? _payloadClipboard;
    private readonly ClipboardHistory _history;
    private readonly ClipboardPayloadHistory? _payloadHistory;
    private readonly IClipboardPicker _picker;
    private readonly IClipboardPayloadPicker? _payloadPicker;
    private readonly IKeyboardOutput _keyboard;
    private readonly bool _historyEnabled;
    private readonly bool _imagesEnabled;
    private string? _captured;
    private bool _disposed;

    public WindowsClipboardController(
        IClipboardService clipboard,
        ClipboardHistory history,
        IClipboardPicker picker,
        IKeyboardOutput keyboard,
        ClipboardPayloadHistory? payloadHistory = null,
        IClipboardPayloadPicker? payloadPicker = null,
        bool historyEnabled = true,
        bool imagesEnabled = true)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _payloadClipboard = clipboard as IClipboardPayloadService;
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _payloadHistory = payloadHistory;
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _payloadPicker = payloadPicker;
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _historyEnabled = historyEnabled;
        _imagesEnabled = imagesEnabled;
        _clipboard.Changed += OnClipboardChanged;
        CaptureCurrentClipboard();
    }

    public ClipboardHistory History => _history;
    public ClipboardPayloadHistory? PayloadHistory => _payloadHistory;

    public bool ShowPickerAndPaste()
    {
        ThrowIfDisposed();
        if (!_historyEnabled)
            return false;

        if (_payloadHistory is not null && _payloadPicker is not null && _payloadClipboard is not null)
        {
            var payloadSnapshot = _payloadHistory.Items;
            if (payloadSnapshot.Count > 0)
            {
                var selectedPayloadIndex = _payloadPicker.Pick(payloadSnapshot);
                if (selectedPayloadIndex is null)
                    return false;
                if ((uint)selectedPayloadIndex.Value >= (uint)payloadSnapshot.Count)
                    throw new InvalidOperationException("Clipboard payload picker returned an invalid index.");

                PastePayload(_payloadHistory.Promote(selectedPayloadIndex.Value));
                return true;
            }
        }

        var snapshot = _history.Items;
        if (snapshot.Count == 0)
            return false;

        var selectedIndex = _picker.Pick(snapshot);
        if (selectedIndex is null)
            return false;
        if ((uint)selectedIndex.Value >= (uint)snapshot.Count)
            throw new InvalidOperationException("Clipboard picker returned an invalid index.");

        PasteText(snapshot[selectedIndex.Value]);
        return true;
    }

    public bool CaptureLatest()
    {
        ThrowIfDisposed();
        if (!_historyEnabled)
            return false;
        _captured = _history.Items.FirstOrDefault();
        return _captured is not null;
    }

    public bool PasteCaptured()
    {
        ThrowIfDisposed();
        if (!_historyEnabled || _captured is null)
            return false;
        PasteText(_captured);
        return true;
    }

    public void CaptureCurrentClipboard()
    {
        ThrowIfDisposed();
        RecordCurrentClipboard();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _clipboard.Changed -= OnClipboardChanged;
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
            RecordCurrentClipboard();
    }

    private void RecordCurrentClipboard()
    {
        if (!_historyEnabled)
            return;

        if (_payloadClipboard is not null && _payloadHistory is not null)
        {
            var payload = _payloadClipboard.ReadPayload();
            if (payload is not null)
            {
                if (payload.Kind == ClipboardPayloadKind.Image && !_imagesEnabled)
                    return;

                _payloadHistory.Record(payload);
                if (payload.Kind == ClipboardPayloadKind.Text)
                    _history.Record(payload.GetText());
                return;
            }
        }

        _history.Record(_clipboard.ReadText());
    }

    private void PastePayload(ClipboardPayload payload)
    {
        if (_payloadClipboard is null)
            throw new InvalidOperationException("Clipboard service does not support binary payloads.");
        if (payload.Kind == ClipboardPayloadKind.Image && !_imagesEnabled)
            return;

        _payloadHistory?.Record(payload);
        if (payload.Kind == ClipboardPayloadKind.Text)
            _history.Record(payload.GetText());
        _payloadClipboard.WritePayload(payload);
        SendPasteShortcut();
    }

    private void PasteText(string text)
    {
        _history.Record(text);
        _clipboard.WriteText(text);
        SendPasteShortcut();
    }

    private void SendPasteShortcut()
    {
        var shift = new KeyboardKey(VkShift, 0);
        var insert = new KeyboardKey(VkInsert, 0, true);
        _keyboard.SendKey(shift, KeyEventKind.Down);
        try
        {
            _keyboard.SendKeyPress(insert);
        }
        finally
        {
            _keyboard.SendKey(shift, KeyEventKind.Up);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
