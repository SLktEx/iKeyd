using iKeyd.Core.Clipboard;
using iKeyd.Core.Input;
using iKeyd.Windows.Clipboard;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ClipboardPayloadControllerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Picker_selection_preserves_displayed_payload_when_live_history_changes(
        bool selectImage, bool evictSelected)
    {
        var clipboard = new FakePayloadClipboardService();
        var persistence = new RecordingHistoryPersistence();
        var history = new ClipboardPayloadHistory(evictSelected ? 2 : 3, persistence);
        var selected = selectImage
            ? ClipboardPayload.FromImage([1, 2, 3, 4], "image/png")
            : ClipboardPayload.FromText("selected text");
        history.Record(selected);
        history.Record(ClipboardPayload.FromText("latest before opening"));
        var keyboard = new RecordingKeyboardOutput();
        var picker = new FakePayloadPicker(1, () =>
        {
            clipboard.Payload = ClipboardPayload.FromText("copied while picker is open");
            clipboard.RaiseChanged();
        });

        using var controller = new WindowsClipboardController(
            clipboard, new ClipboardHistory(), new FakeTextPicker(), keyboard, history, picker);

        Assert.True(controller.ShowPickerAndPaste());

        Assert.Same(selected, picker.SeenItems[1]);
        Assert.Same(selected, clipboard.Payload);
        Assert.Same(selected, history.Items[0]);
        Assert.Same(selected, persistence.SavedItems[0]);
        Assert.Equal(history.Items, persistence.SavedItems);
        Assert.Equal(history.Capacity, history.Count);
        Assert.Single(keyboard.Presses);
    }

    [Fact]
    public void Cancelling_payload_picker_after_history_changes_does_not_paste_a_different_entry()
    {
        var clipboard = new FakePayloadClipboardService();
        var history = new ClipboardPayloadHistory();
        history.Record(ClipboardPayload.FromText("visible entry"));
        var incoming = ClipboardPayload.FromText("copied while picker is open");
        var keyboard = new RecordingKeyboardOutput();
        var picker = new FakePayloadPicker(null, () =>
        {
            clipboard.Payload = incoming;
            clipboard.RaiseChanged();
        });
        using var controller = new WindowsClipboardController(
            clipboard, new ClipboardHistory(), new FakeTextPicker(), keyboard, history, picker);

        Assert.False(controller.ShowPickerAndPaste());

        Assert.Same(incoming, clipboard.Payload);
        Assert.Empty(keyboard.Events);
        Assert.Empty(keyboard.Presses);
    }

    [Fact]
    public void Image_history_selection_is_restored_and_pasted()
    {
        var clipboard = new FakePayloadClipboardService();
        var payloadHistory = new ClipboardPayloadHistory();
        var oldest = ClipboardPayload.FromImage([1, 2, 3, 4], "image/png");
        var latest = ClipboardPayload.FromText("latest");
        payloadHistory.Record(oldest);
        payloadHistory.Record(latest);
        var keyboard = new RecordingKeyboardOutput();
        var payloadPicker = new FakePayloadPicker(1);

        using var controller = new WindowsClipboardController(
            clipboard,
            new ClipboardHistory(),
            new FakeTextPicker(),
            keyboard,
            payloadHistory,
            payloadPicker);

        Assert.True(controller.ShowPickerAndPaste());

        Assert.NotNull(clipboard.Payload);
        Assert.Equal(ClipboardPayloadKind.Image, clipboard.Payload!.Kind);
        Assert.Equal(oldest.Data, clipboard.Payload.Data);
        Assert.Equal(ClipboardPayloadKind.Image, payloadHistory.Items[0].Kind);
        Assert.Equal(2, keyboard.Events.Count);
        Assert.Single(keyboard.Presses);
        Assert.Equal(new KeyboardKey(0x2D, 0, true), keyboard.Presses[0]);
    }

    [Fact]
    public void Clipboard_change_records_image_in_binary_history_only()
    {
        var image = ClipboardPayload.FromImage([9, 8, 7], "image/png");
        var clipboard = new FakePayloadClipboardService { Payload = image };
        var payloadHistory = new ClipboardPayloadHistory();
        var textHistory = new ClipboardHistory();

        using var controller = new WindowsClipboardController(
            clipboard,
            textHistory,
            new FakeTextPicker(),
            new RecordingKeyboardOutput(),
            payloadHistory,
            new FakePayloadPicker(null));

        var saved = Assert.Single(payloadHistory.Items);
        Assert.Equal(ClipboardPayloadKind.Image, saved.Kind);
        Assert.Equal(image.Data, saved.Data);
        Assert.Empty(textHistory.Items);
    }

    [Fact]
    public void Disabled_image_history_ignores_images_but_keeps_text_history()
    {
        var image = ClipboardPayload.FromImage([9, 8, 7], "image/png");
        var clipboard = new FakePayloadClipboardService { Payload = image };
        var payloadHistory = new ClipboardPayloadHistory();
        var textHistory = new ClipboardHistory();

        using var controller = new WindowsClipboardController(
            clipboard,
            textHistory,
            new FakeTextPicker(),
            new RecordingKeyboardOutput(),
            payloadHistory,
            new FakePayloadPicker(null),
            historyEnabled: true,
            imagesEnabled: false);

        Assert.Empty(payloadHistory.Items);
        Assert.Empty(textHistory.Items);

        clipboard.Payload = ClipboardPayload.FromText("still-recorded");
        clipboard.RaiseChanged();

        Assert.Equal("still-recorded", Assert.Single(textHistory.Items));
        Assert.Equal(ClipboardPayloadKind.Text, Assert.Single(payloadHistory.Items).Kind);
    }

    [Fact]
    public void Disabled_history_does_not_collect_or_open_picker()
    {
        var clipboard = new FakePayloadClipboardService { Payload = ClipboardPayload.FromText("secret") };
        var payloadHistory = new ClipboardPayloadHistory();
        var textHistory = new ClipboardHistory();

        using var controller = new WindowsClipboardController(
            clipboard,
            textHistory,
            new FakeTextPicker(),
            new RecordingKeyboardOutput(),
            payloadHistory,
            new FakePayloadPicker(0),
            historyEnabled: false,
            imagesEnabled: true);

        Assert.Empty(payloadHistory.Items);
        Assert.Empty(textHistory.Items);
        Assert.False(controller.ShowPickerAndPaste());
        Assert.False(controller.CaptureLatest());
    }

    private sealed class FakePayloadClipboardService : IClipboardService, IClipboardPayloadService
    {
        public event EventHandler? Changed;
        public ClipboardPayload? Payload { get; set; }

        public string? ReadText()
            => Payload?.Kind == ClipboardPayloadKind.Text ? Payload.GetText() : null;

        public ClipboardPayload? ReadPayload() => Payload;

        public void WriteText(string text)
        {
            Payload = ClipboardPayload.FromText(text);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void WritePayload(ClipboardPayload payload)
        {
            Payload = payload;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }

    private sealed class FakeTextPicker : IClipboardPicker
    {
        public int? Pick(IReadOnlyList<string> items) => null;
    }

    private sealed class FakePayloadPicker(int? result, Action? beforeSelection = null) : IClipboardPayloadPicker
    {
        public IReadOnlyList<ClipboardPayload> SeenItems { get; private set; } = [];

        public int? Pick(IReadOnlyList<ClipboardPayload> items)
        {
            SeenItems = items;
            beforeSelection?.Invoke();
            return result;
        }
    }

    private sealed class RecordingHistoryPersistence : IClipboardHistoryPersistence
    {
        public IReadOnlyList<ClipboardPayload> SavedItems { get; private set; } = [];
        public IReadOnlyList<ClipboardPayload> Load() => [];
        public void Save(IReadOnlyList<ClipboardPayload> items) => SavedItems = items.ToArray();
        public void Clear() => SavedItems = [];
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
