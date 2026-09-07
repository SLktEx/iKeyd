using System.Text.Json;
using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class DesktopFunctionLayerParityTests
{
    private static string RuntimeFixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");

    public static IEnumerable<object[]> PinnedDesktopCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        foreach (var item in document.RootElement.GetProperty("desktopCases").EnumerateArray())
        {
            yield return new object[]
            {
                item.GetProperty("function").GetString() ?? string.Empty,
                item.GetProperty("state").GetString() ?? string.Empty,
                item.GetProperty("modernAction").GetString() ?? string.Empty
            };
        }
    }

    [Fact]
    public void Pinned_desktop_fixture_has_the_complete_15_case_set()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        var actual = document.RootElement.GetProperty("desktopCases")
            .EnumerateArray()
            .Select(item => $"{item.GetProperty("function").GetString()}:{item.GetProperty("state").GetString()}:{item.GetProperty("modernAction").GetString()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "E:M:Minimize",
            "E:MH:Win+Up",
            "E:HM:Win+Down",
            "R:M:ToggleMaximizeRestore",
            "R:MH:Win+Right",
            "R:HM:Win+Left",
            "R:MS:Win+R",
            "T:M:ToggleTopmost",
            "T:MH:Transparency-30",
            "T:HM:Transparency+30",
            "T:MS:ToggleTitleBar",
            "G:MH:Ctrl+Tab",
            "G:HM:Ctrl+Shift+Tab",
            "B:MH:Alt+Esc",
            "B:HM:Alt+Shift+Esc"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(PinnedDesktopCases))]
    public async Task Every_pinned_desktop_function_is_reachable_through_the_runtime(
        string function,
        string state,
        string modernAction)
    {
        var scenario = new CompatibilityScenario
        {
            Id = $"desktop-{function.ToLowerInvariant()}-{state.ToLowerInvariant()}",
            InitialState = new ScenarioInitialState
            {
                Mode = "S",
                Ime = "off",
                Layers = state.Select(value => value.ToString()).ToList(),
                Modifiers = []
            },
            Input =
            [
                new ScenarioInputEvent { AtMs = 100, Kind = "keyDown", Key = function },
                new ScenarioInputEvent { AtMs = 110, Kind = "keyUp", Key = function }
            ],
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "function-dispatch", "desktop"],
            RequiredEnvironment = ["windows"],
            OracleTargets = ["ikeyd-runtime"]
        };

        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);
        var expected = Expected(modernAction);

        Assert.Null(actual.Text);
        Assert.Equal(expected.Events, CanonicalEvents(actual.Events));
        Assert.Equal(expected.Actions, CanonicalActions(actual.Actions));
    }

    private static Observation Expected(string modernAction)
        => modernAction switch
        {
            "Minimize" => Action("window:minimize"),
            "Win+Up" => Action("window:move-resize:0,0,1920,540"),
            "Win+Down" => Action("window:move-resize:0,540,1920,540"),
            "ToggleMaximizeRestore" => Action("window:maximize"),
            "Win+Right" => Action("window:move-resize:960,0,960,1080"),
            "Win+Left" => Action("window:move-resize:0,0,960,1080"),
            "Win+R" => Keys("LWin", "R"),
            "ToggleTopmost" => Action("window:topmost:true"),
            "Transparency-30" => Action("window:opacity:225"),
            "Transparency+30" => Action("window:opacity:off"),
            "ToggleTitleBar" => Action("window:caption:false"),
            "Ctrl+Tab" => Keys("Control", "Tab"),
            "Ctrl+Shift+Tab" => Keys("Control", "Shift", "Tab"),
            "Alt+Esc" => Keys("Alt", "Escape"),
            "Alt+Shift+Esc" => Keys("Alt", "Shift", "Escape"),
            _ => throw new InvalidDataException($"Unknown pinned desktop action '{modernAction}'.")
        };

    private static Observation Action(string action)
        => new([], [action]);

    private static Observation Keys(params string[] keys)
    {
        var events = new List<string>();
        foreach (var key in keys)
            events.Add($"keyDown:{key}");
        foreach (var key in keys)
            events.Add($"keyUp:{key}");
        return new(events.ToArray(), []);
    }

    private static string[] CanonicalEvents(IReadOnlyList<ObservedKeyEvent> events)
        => events.Select(item => $"{item.Kind}:{CanonicalKey(item.Key)}").ToArray();

    private static string CanonicalKey(string key)
        => key.ToUpperInvariant() switch
        {
            "VK_A0" or "VK_A1" or "SHIFT" => "Shift",
            "VK_A2" or "VK_A3" or "CONTROL" or "CTRL" => "Control",
            "VK_A4" or "VK_A5" or "ALT" => "Alt",
            "VK_5B" or "LWIN" => "LWin",
            "ESC" or "ESCAPE" => "Escape",
            _ => key
        };

    private static string[] CanonicalActions(IReadOnlyList<ObservedAction> actions)
        => actions.Select(item => $"{item.Kind}:{item.Value}").ToArray();

    private sealed record Observation(string[] Events, string[] Actions);
}
