using System.Text.Json;
using iKeyd.Windows.Clipboard;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class RealWindowsClipboardE2ETests
{
    public const string EnvironmentVariable = "IKEYD_REAL_WINDOWS_CLIPBOARD_E2E";
    public const string ResultPathEnvironmentVariable = "IKEYD_REAL_WINDOWS_CLIPBOARD_RESULT";

    [Fact]
    [Trait("Category", "RealWindowsClipboardE2E")]
    public void Text_clipboard_round_trips_listener_and_restores_original_content()
    {
        if (!IsEnabled())
            return;

        var resultPath = Environment.GetEnvironmentVariable(ResultPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(resultPath))
            throw new InvalidOperationException($"Set {ResultPathEnvironmentVariable} before running the clipboard E2E.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);

        RunSta(() =>
        {
            var original = CaptureSafeOriginal();
            if (!original.CanRestore)
            {
                WriteResult(resultPath, "skipped", original.Reason ?? "Clipboard contains a non-text format; no mutation was attempted.");
                return;
            }

            using var service = new WindowsClipboardService();
            using var changed = new ManualResetEventSlim(false);
            EventHandler handler = (_, _) => changed.Set();
            service.Changed += handler;
            var marker = $"iKeyd-#59-{Guid.NewGuid():N}";

            try
            {
                changed.Reset();
                service.WriteText(marker);
                Assert.True(changed.Wait(TimeSpan.FromSeconds(3)), "Clipboard listener did not observe the test write.");
                Assert.Equal(marker, service.ReadText());

                var payload = service.ReadPayload();
                Assert.NotNull(payload);
                Assert.Equal(marker, payload!.GetText());

                WriteResult(resultPath, "pass", "Unicode text write/read, WM_CLIPBOARDUPDATE listener, and payload read verified; original clipboard restored.");
            }
            finally
            {
                service.Changed -= handler;
                RestoreOriginal(original);
            }
        });
    }

    private static ClipboardOriginal CaptureSafeOriginal()
    {
        var dataObject = System.Windows.Forms.Clipboard.GetDataObject();
        var formats = dataObject?.GetFormats(autoConvert: false) ?? [];
        var containsText = System.Windows.Forms.Clipboard.ContainsText(TextDataFormat.UnicodeText);
        if (formats.Length > 0 && !containsText)
            return new ClipboardOriginal(false, null, "Clipboard contains non-text/custom data.");

        var text = containsText
            ? System.Windows.Forms.Clipboard.GetText(TextDataFormat.UnicodeText)
            : null;
        return new ClipboardOriginal(true, text, null);
    }

    private static void RestoreOriginal(ClipboardOriginal original)
    {
        if (!original.CanRestore)
            return;
        if (string.IsNullOrEmpty(original.Text))
            System.Windows.Forms.Clipboard.Clear();
        else
            System.Windows.Forms.Clipboard.SetText(original.Text, TextDataFormat.UnicodeText);
    }

    private static void WriteResult(string path, string status, string message)
    {
        var payload = new { status, message };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsEnabled()
        => OperatingSystem.IsWindows() &&
           string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal);

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                done.Set();
            }
        })
        {
            IsBackground = true,
            Name = "iKeyd.RealWindowsClipboardE2E"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "Real-Windows clipboard E2E timed out.");
        thread.Join(TimeSpan.FromSeconds(2));
        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"Real-Windows clipboard E2E failed: {failure}");
    }

    private sealed record ClipboardOriginal(bool CanRestore, string? Text, string? Reason);
}
