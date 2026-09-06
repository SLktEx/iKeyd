using iKeyd.Core.Clipboard;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ClipboardPayloadHistoryTests
{
    [Fact]
    public void Loads_persisted_items_and_keeps_capacity()
    {
        var persistence = new FakePersistence(
        [
            ClipboardPayload.FromText("one"),
            ClipboardPayload.FromText("two"),
            ClipboardPayload.FromText("three")
        ]);

        var history = new ClipboardPayloadHistory(2, persistence);

        Assert.Equal(2, history.Count);
        Assert.Equal("one", history.Items[0].GetText());
        Assert.Equal("two", history.Items[1].GetText());
    }

    [Fact]
    public void Image_payload_is_persisted_without_text_conversion()
    {
        var persistence = new FakePersistence();
        var history = new ClipboardPayloadHistory(20, persistence);
        var image = ClipboardPayload.FromImage([0x89, 0x50, 0x4E, 0x47], "image/png");

        Assert.True(history.Record(image));

        var saved = Assert.Single(persistence.Saved);
        Assert.Equal(ClipboardPayloadKind.Image, saved.Kind);
        Assert.Equal("image/png", saved.ContentType);
        Assert.Equal(image.Data, saved.Data);
    }

    [Fact]
    public void Recording_duplicate_payload_promotes_without_duplicate()
    {
        var persistence = new FakePersistence();
        var history = new ClipboardPayloadHistory(20, persistence);
        var one = ClipboardPayload.FromText("one");
        var two = ClipboardPayload.FromText("two");

        history.Record(one);
        history.Record(two);
        Assert.True(history.Record(one));

        Assert.Equal(2, history.Count);
        Assert.Equal("one", history.Items[0].GetText());
        Assert.Equal("two", history.Items[1].GetText());
    }

    private sealed class FakePersistence(IReadOnlyList<ClipboardPayload>? initial = null)
        : IClipboardHistoryPersistence
    {
        private readonly IReadOnlyList<ClipboardPayload> _initial = initial ?? [];

        public IReadOnlyList<ClipboardPayload> Saved { get; private set; } = [];

        public IReadOnlyList<ClipboardPayload> Load() => _initial;

        public void Save(IReadOnlyList<ClipboardPayload> items)
            => Saved = items.ToArray();

        public void Clear() => Saved = [];
    }
}
