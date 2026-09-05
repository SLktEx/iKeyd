using iKeyd.Core.Clipboard;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Clipboard;

public sealed class WindowsClipboardController : IDisposable
{
    private const ushort VkShift = 0x10;
    private const ushort VkInsert = 0x2D;

    private readonly IClipboardService _clipboard;
    private readonly ClipboardHistory _history;
    private readonly IClipboardPicker _picker;
    private readonly IKeyboardOutput _keyboard;
    private string? _captured;
    private string? _suppressedWriteValue;
    private bool _suppressNextMatchingClipboardChange;
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

        // Legacy hotkeySKG records the selected row by index when the clipboard
        // write notification arrives. Promote that exact row synchronously and
        // suppress our own matching notification so duplicate values keep the
        // same index-sensitive behavior without being inserted twice.
        var text = _history.Promote(selectedIndex.Value);
        WriteClipboardAndPaste(text);
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

        var snapshot = _history.Items;
        var index = Array.FindIndex(snapshot.ToArray(), item => string.Equals(item, _captured, StringComparison.Ordinal));
        if (index >= 0)
            _history.Promote(index);
        else
            _history.Record(_captured);

        WriteClipboardAndPaste(_captured);
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
        if (_disposed)
            return;

        var text = _clipboard.ReadText();
        if (_suppressNextMatchingClipboardChange)
        {
            var suppress = string.Equals(text, _suppressedWriteValue, StringComparison.Ordinal);
            _suppressNextMatchingClipboardChange = false;
            _suppressedWriteValue = null;
            if (suppress)
                return;
        }

        _history.Record(text);
    }

    private void WriteClipboardAndPaste(string text)
    {
        _suppressedWriteValue = text;
        _suppressNextMatchingClipboardChange = true;
        try
        {
            _clipboard.WriteText(text);
        }
        catch
        {
            _suppressNextMatchingClipboardChange = false;
            _suppressedWriteValue = null;
            throw;
        }

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
