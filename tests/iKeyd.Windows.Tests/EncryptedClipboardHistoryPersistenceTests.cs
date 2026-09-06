using iKeyd.Core.Clipboard;
using iKeyd.Windows.Clipboard;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class EncryptedClipboardHistoryPersistenceTests
{
    [Fact]
    public void Text_and_image_history_round_trip_without_plaintext_on_disk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "iKeyd-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03 };
            using (var persistence = new WindowsClipboardHistoryPersistence(directory))
            {
                var history = new ClipboardPayloadHistory(20, persistence);
                history.Record(ClipboardPayload.FromText("super-secret-clipboard-text"));
                history.Record(ClipboardPayload.FromImage(imageBytes, "image/png"));
            }

            var historyPath = Path.Combine(directory, "clipboard-history.json");
            var keyPath = Path.Combine(directory, "clipboard-history.key");
            Assert.True(File.Exists(historyPath));
            Assert.True(File.Exists(keyPath));
            Assert.DoesNotContain("super-secret-clipboard-text", File.ReadAllText(historyPath), StringComparison.Ordinal);

            using var reloadedPersistence = new WindowsClipboardHistoryPersistence(directory);
            var reloaded = new ClipboardPayloadHistory(20, reloadedPersistence);

            Assert.Equal(2, reloaded.Count);
            Assert.Equal(ClipboardPayloadKind.Image, reloaded.Items[0].Kind);
            Assert.Equal("image/png", reloaded.Items[0].ContentType);
            Assert.Equal(imageBytes, reloaded.Items[0].Data);
            Assert.Equal("super-secret-clipboard-text", reloaded.Items[1].GetText());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
