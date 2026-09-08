using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LayerTriggerPhysicalRoutingTests
{
    public static IEnumerable<object[]> PhysicalLayerCases()
    {
        yield return Case(
            "convert-tap-control",
            [Down("CONVERT", 10), Up("CONVERT", 20), Down("Q", 30), Up("Q", 40)],
            Press("Control"));

        yield return Case(
            "space-tap-space",
            [Down("SPACE", 10), Up("SPACE", 20), Down("Q", 30), Up("Q", 40)],
            Press("Space"));

        yield return Case(
            "m-kana-muhenkan",
            [Down("NONCONVERT", 10), Down("KANA", 20), Up("KANA", 30), Up("NONCONVERT", 40), Down("Q", 50), Up("Q", 60)],
            Press("NonConvert"));

        yield return Case(
            "h-kana-henkan",
            [Down("CONVERT", 10), Down("KANA", 20), Up("KANA", 30), Up("CONVERT", 40), Down("Q", 50), Up("Q", 60)],
            Press("Convert"));

        yield return Case(
            "s-kana-ctrl-esc",
            [Down("SPACE", 10), Down("KANA", 20), Up("KANA", 30), Up("SPACE", 40), Down("Q", 50), Up("Q", 60)],
            Keys("Control", "Escape"));

        yield return Case(
            "kana-toggle-k-then-key-clears",
            [Down("KANA", 10), Up("KANA", 20), Down("Q", 30), Up("Q", 40), Down("W", 50), Up("W", 60)],
            Keys("LWin", "Q"));

        yield return Case(
            "alt-kana-a-then-key-clears",
            [Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40), Down("Q", 50), Up("Q", 60), Down("W", 70), Up("W", 80)],
            Keys("Alt", "Q"));

        yield return Case(
            "alt-convert-direct-ah",
            [Down("ALT", 10), Down("CONVERT", 20), Up("ALT", 30), Down("Q", 40), Up("Q", 50), Up("CONVERT", 60), Down("W", 70), Up("W", 80)],
            Keys("Alt", "Control", "Q"));

        yield return Case(
            "alt-space-direct-as",
            [Down("ALT", 10), Down("SPACE", 20), Up("ALT", 30), Down("Q", 40), Up("Q", 50), Up("SPACE", 60), Down("W", 70), Up("W", 80)],
            Keys("Alt", "Shift", "Q"));

        yield return Case(
            "alt-space-release-after-alt-up-cleans-state",
            [Down("ALT", 10), Down("SPACE", 20), Up("ALT", 30), Up("SPACE", 40), Down("Q", 50), Up("Q", 60)],
            []);

        yield return Case(
            "kh-h-up-henkan",
            [Down("KANA", 10), Up("KANA", 20), Down("CONVERT", 30), Up("CONVERT", 40), Down("Q", 50), Up("Q", 60)],
            Press("Convert"));

        yield return Case(
            "ks-space-up-shift-space",
            [Down("KANA", 10), Up("KANA", 20), Down("SPACE", 30), Up("SPACE", 40), Down("Q", 50), Up("Q", 60)],
            Keys("Shift", "Space"));

        yield return Case(
            "kms-space-up-ctrl-enter",
            [Down("KANA", 10), Up("KANA", 20), Down("NONCONVERT", 30), Down("SPACE", 40), Up("SPACE", 50), Up("NONCONVERT", 60), Down("Q", 70), Up("Q", 80)],
            Keys("Control", "Enter"));

        yield return Case(
            "ams-space-up-alt-enter",
            [Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40), Down("NONCONVERT", 50), Down("SPACE", 60), Up("SPACE", 70), Up("NONCONVERT", 80), Down("Q", 90), Up("Q", 100)],
            Keys("Alt", "Enter"));

        yield return Case(
            "am-alt-space-up-alt-space",
            [Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40), Down("NONCONVERT", 50), Down("ALT", 60), Down("SPACE", 70), Up("ALT", 80), Up("SPACE", 90), Up("NONCONVERT", 100), Down("Q", 110), Up("Q", 120)],
            Keys("Alt", "Space"));
    }

    [Theory]
    [MemberData(nameof(PhysicalLayerCases))]
    public async Task Physical_K_A_and_alt_variant_routes_leave_expected_output_and_no_stuck_layer(
        string name,
        ScenarioInputEvent[] input,
        string[] expectedEvents)
    {
        var scenario = new CompatibilityScenario
        {
            Id = $"layer-trigger-{name}",
            InitialState = new ScenarioInitialState
            {
                Mode = "R",
                Ime = "off",
                Layers = [],
                Modifiers = []
            },
            Input = input.ToList(),
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "layer-trigger", "physical"],
            RequiredEnvironment = ["windows"],
            OracleTargets = ["ikeyd-runtime"]
        };

        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);

        Assert.Null(actual.Text);
        Assert.Empty(actual.Actions);
        Assert.Equal(expectedEvents, CanonicalEvents(actual.Events));
    }

    private static object[] Case(string name, ScenarioInputEvent[] input, string[] expected)
        => [name, input, expected];

    private static ScenarioInputEvent Down(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };

    private static string[] Press(string key)
        => [$"keyDown:{key}", $"keyUp:{key}"];

    private static string[] Keys(params string[] keys)
    {
        var events = new List<string>();
        foreach (var key in keys)
            events.Add($"keyDown:{key}");

        events.Add($"keyUp:{keys[^1]}");
        for (var index = 0; index < keys.Length - 1; index++)
            events.Add($"keyUp:{keys[index]}");
        return events.ToArray();
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
            "VK_1C" or "CONVERT" or "HENKAN" => "Convert",
            "VK_1D" or "NONCONVERT" or "MUHENKAN" => "NonConvert",
            "ESC" or "ESCAPE" => "Escape",
            _ => key
        };
}
