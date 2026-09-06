using iKeyd.App;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class InputDiagnosticsAutoLogTests
{
    [Fact]
    public void Default_path_is_under_local_app_data_and_is_stable()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iKeyd",
            "logs");

        Assert.Equal(expectedRoot, InputDiagnosticsAutoLog.DefaultLogDirectory);
        Assert.Equal(
            Path.Combine(expectedRoot, "input-diagnostics.log"),
            InputDiagnosticsAutoLog.DefaultLogPath);
    }

    [Fact]
    public void Existing_current_log_is_rotated_and_current_snapshot_is_atomically_replaced()
    {
        var root = Path.Combine(Path.GetTempPath(), "ikeyd-diagnostics-auto-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var current = Path.Combine(root, "input-diagnostics.log");
        File.WriteAllText(current, "previous-session");

        var snapshot = "snapshot-1";
        try
        {
            using var log = new InputDiagnosticsAutoLog(
                () => snapshot,
                current,
                TimeSpan.FromHours(1));

            Assert.False(File.Exists(current));
            Assert.True(File.Exists(log.PreviousLogPath));
            Assert.Equal("previous-session", File.ReadAllText(log.PreviousLogPath));

            log.FlushNow();
            Assert.Equal("snapshot-1", File.ReadAllText(current));
            Assert.False(File.Exists(current + ".tmp"));

            snapshot = "snapshot-2";
            log.FlushNow();
            Assert.Equal("snapshot-2", File.ReadAllText(current));
            Assert.Equal("previous-session", File.ReadAllText(log.PreviousLogPath));
            Assert.False(File.Exists(current + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Dispose_flushes_the_latest_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ikeyd-diagnostics-auto-log-tests", Guid.NewGuid().ToString("N"));
        var current = Path.Combine(root, "input-diagnostics.log");
        var snapshot = "before-dispose";

        try
        {
            var log = new InputDiagnosticsAutoLog(
                () => snapshot,
                current,
                TimeSpan.FromHours(1));

            snapshot = "final-snapshot";
            log.Dispose();

            Assert.Equal("final-snapshot", File.ReadAllText(current));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
