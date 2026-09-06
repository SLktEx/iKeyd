using iKeyd.Core.Clipboard;
using iKeyd.Core.Input;
using iKeyd.Windows.Clipboard;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ClipboardPayloadControllerTests
{
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

        public void Dispose() { }
    }

    private sealed class FakeTextPicker : IClipboardPicker
    {
        public int? Pick(IReadOnlyList<string> items) => null;
    }

    private sealed class FakePayloadPicker(int? result) : IClipboardPayloadPicker
    {
        public int? Pick(IReadOnlyList<ClipboardPayload> items) => result;
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
