using iKeyd.Core.Clipboard;
using iKeyd.Core.Input;
using iKeyd.Windows.Clipboard;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ClipboardControllerTests
{
    [Fact]
    public void Constructor_captures_current_clipboard_and_change_events_update_history()
    {
        using var clipboard = new FakeClipboardService { Text = "initial" };
        var history = new ClipboardHistory();
        using var controller = new WindowsClipboardController(
            clipboard,
            history,
            new FakePicker(null),
            new RecordingKeyboardOutput());

        Assert.Equal(["initial"], history.Items);

        clipboard.Text = "next";
        clipboard.RaiseChanged();

        Assert.Equal(["next", "initial"], history.Items);
    }

    [Fact]
    public void Picker_selection_is_promoted_written_and_pasted_with_shift_insert()
    {
        using var clipboard = new FakeClipboardService();
        var history = new ClipboardHistory();
        history.Record("oldest");
        history.Record("middle");
        history.Record("latest");
        var keyboard = new RecordingKeyboardOutput();
        var picker = new FakePicker(2);
        using var controller = new WindowsClipboardController(clipboard, history, picker, keyboard);

        Assert.True(controller.ShowPickerAndPaste());

        Assert.Equal(["oldest", "latest", "middle"], history.Items);
        Assert.Equal("oldest", clipboard.Text);
        Assert.Equal(["latest", "middle", "oldest"], picker.SeenItems);
        Assert.Equal(2, keyboard.Events.Count);
        Assert.Equal(new KeyboardKey(0x10, 0), keyboard.Events[0].Key);
        Assert.Equal(KeyEventKind.Down, keyboard.Events[0].Kind);
        Assert.Equal(KeyEventKind.Up, keyboard.Events[1].Kind);
        Assert.Equal([new KeyboardKey(0x2D, 0, true)], keyboard.Presses);
    }

    [Fact]
    public void Cancelling_picker_does_not_change_clipboard_or_send_keys()
    {
        using var clipboard = new FakeClipboardService();
        var history = new ClipboardHistory();
        history.Record("one");
        var keyboard = new RecordingKeyboardOutput();
        using var controller = new WindowsClipboardController(
            clipboard,
            history,
            new FakePicker(null),
            keyboard);

        Assert.False(controller.ShowPickerAndPaste());

        Assert.Null(clipboard.Text);
        Assert.Empty(keyboard.Events);
        Assert.Empty(keyboard.Presses);
    }

    [Fact]
    public void Capture_and_paste_preserves_the_legacy_two_step_clipboard_action()
    {
        using var clipboard = new FakeClipboardService();
        var history = new ClipboardHistory();
        history.Record("captured");
        var keyboard = new RecordingKeyboardOutput();
        using var controller = new WindowsClipboardController(
            clipboard,
            history,
            new FakePicker(null),
            keyboard);

        Assert.True(controller.CaptureLatest());
        clipboard.Text = "other";
        clipboard.RaiseChanged();
        Assert.Equal("other", history.Items[0]);

        Assert.True(controller.PasteCaptured());

        Assert.Equal("captured", clipboard.Text);
        Assert.Equal("captured", history.Items[0]);
        Assert.Single(keyboard.Presses);
    }

    [Fact]
    public void Picker_preview_flattens_control_whitespace_without_changing_source_data()
    {
        var preview = WindowsClipboardPicker.BuildPreview("a\r\nb\tc");

        Assert.Equal("a ↵ b ⇥ c", preview);
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public event EventHandler? Changed;
        public string? Text { get; set; }
        public string? ReadText() => Text;
        public void WriteText(string text)
        {
            Text = text;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }

    private sealed class FakePicker(int? result) : IClipboardPicker
    {
        public IReadOnlyList<string> SeenItems { get; private set; } = [];
        public int? Pick(IReadOnlyList<string> items)
        {
            SeenItems = items.ToArray();
            return result;
        }
    }

    private sealed class RecordingKeyboardOutput : IKeyboardOutput
    {
        public List<(KeyboardKey Key, KeyEventKind Kind)> Events { get; } = [];
        public List<KeyboardKey> Presses { get; } = [];
        public void SendKey(KeyboardKey key, KeyEventKind kind) => Events.Add((key, kind));
        public void SendKeyPress(KeyboardKey key) => Presses.Add(key);
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }
}
