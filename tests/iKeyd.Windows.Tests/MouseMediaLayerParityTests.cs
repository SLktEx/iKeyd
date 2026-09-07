using System.Text.Json;
using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

[Collection(GlobalWindowsInputCollection.Name)]
public sealed class MouseMediaLayerParityTests
{
    private static string RuntimeFixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");

    public static IEnumerable<object[]> PinnedMouseMediaCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        foreach (var item in document.RootElement.GetProperty("mouseMediaCases").EnumerateArray())
        {
            yield return new object[]
            {
                item.GetProperty("key").GetString() ?? string.Empty,
                item.GetProperty("action").GetString() ?? string.Empty
            };
        }
    }

    [Theory]
    [MemberData(nameof(PinnedMouseMediaCases))]
    public async Task Every_pinned_SM_mouse_media_binding_is_reachable_from_physical_keys(
        string fixtureKey,
        string fixtureAction)
    {
        var scenarioKey = ScenarioKey(fixtureKey);
        var scenario = new CompatibilityScenario
        {
            Id = $"sm-{fixtureAction.ToLowerInvariant()}",
            InitialState = new ScenarioInitialState
            {
                Mode = "S",
                Ime = "off",
                Layers = [],
                Modifiers = []
            },
            Input =
            [
                Down("SPACE", 10),
                Down("NONCONVERT", 20),
                Down(scenarioKey, 30),
                Up(scenarioKey, 40),
                Up("NONCONVERT", 50),
                Up("SPACE", 60)
            ],
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "layer-gesture", "sm", "mouse-media"],
            RequiredEnvironment = ["windows"],
            OracleTargets = ["ikeyd-runtime"]
        };

        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);

        Assert.Null(actual.Text);
        Assert.Empty(actual.Events);
        Assert.Equal(new[] { ExpectedAction(fixtureAction) }, CanonicalActions(actual.Actions));
    }

    [Fact]
    public void Pinned_SM_fixture_has_the_complete_legacy_binding_set()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        var actual = document.RootElement.GetProperty("mouseMediaCases")
            .EnumerateArray()
            .Select(item => $"{item.GetProperty("key").GetString()}:{item.GetProperty("action").GetString()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "j:MouseLeft",
            "k:MouseDown",
            "l:MouseRight",
            "i:MouseUp",
            "u:LeftClick",
            "o:RightClick",
            "p:WheelUp",
            ";:WheelDown",
            "@:CtrlWheelUp",
            "::CtrlWheelDown",
            ",:MiddleClick",
            "q:VolumeUp",
            "a:VolumeMute",
            "z:VolumeDown",
            "r:MediaNext",
            "f:MediaPlayPause",
            "v:MediaPrevious"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    private static string ScenarioKey(string fixtureKey)
        => fixtureKey switch
        {
            ";" => "SCOLON",
            ":" => "COLON",
            "@" => "AT",
            "," => "COMMA",
            _ => fixtureKey.ToUpperInvariant()
        };

    private static string ExpectedAction(string fixtureAction)
        => fixtureAction switch
        {
            "MouseLeft" => "mouse:move-by:-1,0",
            "MouseDown" => "mouse:move-by:0,1",
            "MouseRight" => "mouse:move-by:1,0",
            "MouseUp" => "mouse:move-by:0,-1",
            "LeftClick" => "mouse:click:left",
            "RightClick" => "mouse:click:right",
            "WheelUp" => "mouse:scroll:120:plain",
            "WheelDown" => "mouse:scroll:-120:plain",
            "CtrlWheelUp" => "mouse:scroll:120:ctrl",
            "CtrlWheelDown" => "mouse:scroll:-120:ctrl",
            "MiddleClick" => "mouse:click:middle",
            "VolumeUp" => "media:VolumeUp",
            "VolumeMute" => "media:VolumeMute",
            "VolumeDown" => "media:VolumeDown",
            "MediaNext" => "media:NextTrack",
            "MediaPlayPause" => "media:PlayPause",
            "MediaPrevious" => "media:PreviousTrack",
            _ => throw new InvalidDataException($"Unknown pinned mouse/media action '{fixtureAction}'.")
        };

    private static string[] CanonicalActions(IReadOnlyList<ObservedAction> actions)
        => actions.Select(item => $"{item.Kind}:{item.Value}").ToArray();

    private static ScenarioInputEvent Down(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };
}
