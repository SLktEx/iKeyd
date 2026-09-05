using System.Text.Json;
using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyDifferentialComparisonTests
{
    [Fact]
    public void CompareResults_is_empty_for_identical_observations()
    {
        var left = Result("iKeyd.Windows", "fa");
        var right = Result("hotkeySKG.exe", "fa");

        Assert.Empty(LegacyDifferentialComparison.CompareResults(left, right));
    }

    [Fact]
    public void CompareResults_reports_direct_text_mismatch()
    {
        var left = Result("iKeyd.Windows", "fa");
        var right = Result("hotkeySKG.exe", "fi");

        var differences = LegacyDifferentialComparison.CompareResults(left, right);

        Assert.Single(differences);
        Assert.Contains("iKeyd.Windows='fa'", differences[0], StringComparison.Ordinal);
        Assert.Contains("hotkeySKG.exe='fi'", differences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void WriteReport_emits_machine_readable_comparison_with_timeline_and_initial_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ikeyd-diff-{Guid.NewGuid():N}");
        try
        {
            var report = new LegacyDifferentialReport
            {
                ScenarioId = "test-scenario",
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                InitialState = new ScenarioInitialState
                {
                    Mode = "S",
                    Ime = "on",
                    Modifiers = ["Ctrl"]
                },
                Input =
                [
                    new ScenarioInputEvent { AtMs = 0, Kind = "keyDown", Key = "K" },
                    new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "Q" }
                ],
                Expected = new ScenarioExpected { Text = "fa" },
                IKeyd = Result("iKeyd.Windows", "fa"),
                LegacyExe = Result("hotkeySKG.exe", "fi", "abc123"),
                IKeydVsExpected = [],
                LegacyVsExpected = ["text mismatch"],
                IKeydVsLegacy = ["direct mismatch"]
            };

            var path = LegacyDifferentialComparison.WriteReport(report, directory);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            Assert.Equal("test-scenario", root.GetProperty("ScenarioId").GetString());
            Assert.False(root.GetProperty("IsMatch").GetBoolean());
            Assert.Equal("S", root.GetProperty("InitialState").GetProperty("Mode").GetString());
            Assert.Equal("Ctrl", root.GetProperty("InitialState").GetProperty("Modifiers")[0].GetString());
            Assert.Equal(2, root.GetProperty("Input").GetArrayLength());
            Assert.Equal(10, root.GetProperty("Input")[1].GetProperty("AtMs").GetInt64());
            Assert.Equal("fa", root.GetProperty("IKeyd").GetProperty("Text").GetString());
            Assert.Equal("fi", root.GetProperty("LegacyExe").GetProperty("Text").GetString());
            Assert.Equal(
                "abc123",
                root.GetProperty("LegacyExe").GetProperty("Metadata").GetProperty("sha256").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static ScenarioRunResult Result(string runner, string text, string? sha256 = null)
    {
        var metadata = new Dictionary<string, string>();
        if (sha256 is not null)
            metadata["sha256"] = sha256;

        return new ScenarioRunResult
        {
            Runner = runner,
            ScenarioId = "test-scenario",
            Text = text,
            Events = [],
            Metadata = metadata
        };
    }
}
