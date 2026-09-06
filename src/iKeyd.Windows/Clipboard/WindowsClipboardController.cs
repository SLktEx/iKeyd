using iKeyd.Core.Clipboard;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Clipboard;

public sealed class WindowsClipboardController : IClipboardHistoryActions, IDisposable
{
    private const ushort VkShift = 0x10;
    private const ushort VkInsert = 0x2D;

    private readonly IClipboardService _clipboard;
    private readonly ClipboardHistory _history;
    private readonly IClipboardPicker _picker;
    private readonly IKeyboardOutput _keyboard;
    private string? _captured;
    private bool _disposed;

    public WindowsClipboardController(
        IClipboardService clipboard,
        ClipboardHistory history,
        IClipboardPicker picker,
        IKeyboardOutput keyboard)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _clipboard.Changed += OnClipboardChanged;
        CaptureCurrentClipboard();
    }

    public ClipboardHistory History => _history;

    public bool ShowPickerAndPaste()
    {
        ThrowIfDisposed();
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
        _captured = _history.Items.FirstOrDefault();
        return _captured is not null;
    }

    public bool PasteCaptured()
    {
        ThrowIfDisposed();
        if (_captured is null)
            return false;
        PasteText(_captured);
        return true;
    }

    public void CaptureCurrentClipboard()
    {
        ThrowIfDisposed();
        _history.Record(_clipboard.ReadText());
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
            _history.Record(_clipboard.ReadText());
    }

    private void PasteText(string text)
    {
        _history.Record(text);
        _clipboard.WriteText(text);

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
