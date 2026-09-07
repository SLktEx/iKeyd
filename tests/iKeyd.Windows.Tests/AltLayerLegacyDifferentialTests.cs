using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

[Collection(GlobalWindowsInputCollection.Name)]
public sealed class AltLayerLegacyDifferentialTests
{
    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task Physical_Alt_layer_routes_match_both_pinned_legacy_oracles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiled = new LegacyAltLayerGestureScenarioRunner();
        var source = new HostedAutoHotkeySourceRunner(() => new LegacyAltLayerGestureScenarioRunner());
        if (!compiled.IsAvailable || !source.IsAvailable)
            return;

        var runtimeRunner = new IKeydRuntimeScenarioRunner();
        var failures = new List<string>();

        foreach (var scenario in Scenarios())
        {
            var runtime = await runtimeRunner.RunAsync(scenario);
            var exe = await compiled.RunAsync(scenario);
            var ahk = await source.RunAsync(scenario);

            Compare(scenario.Id, "compiled EXE vs AHK source", exe, ahk, failures);
            Compare(scenario.Id, "iKeyd vs compiled EXE", exe, runtime, failures);
        }

        Assert.True(
            failures.Count == 0,
            failures.Count == 0
                ? string.Empty
                : $"{failures.Count} physical Alt-layer mismatches:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, failures));
    }

    private static CompatibilityScenario[] Scenarios()
        =>
        [
            Scenario(
                "physical-alt-kana-one-shot",
                [
                    Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40),
                    Down("Q", 50), Up("Q", 60),
                    Down("CONVERT", 70), Up("CONVERT", 80)
                ]),
            Scenario(
                "physical-alt-convert-release",
                [
                    Down("ALT", 10), Down("CONVERT", 20), Up("CONVERT", 30), Up("ALT", 40),
                    Down("CONVERT", 50), Up("CONVERT", 60)
                ]),
            Scenario(
                "physical-alt-space-from-m",
                [
                    Down("NONCONVERT", 10),
                    Down("ALT", 20), Down("SPACE", 30), Up("SPACE", 40), Up("ALT", 50),
                    Up("NONCONVERT", 60),
                    Down("CONVERT", 70), Up("CONVERT", 80)
                ]),
            Scenario(
                "physical-alt-space-from-am",
                [
                    Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40),
                    Down("NONCONVERT", 50),
                    Down("ALT", 60), Down("SPACE", 70), Up("SPACE", 80), Up("ALT", 90),
                    Up("NONCONVERT", 100),
                    Down("CONVERT", 110), Up("CONVERT", 120)
                ])
        ];

    private static CompatibilityScenario Scenario(string id, ScenarioInputEvent[] input)
        => new()
        {
            Id = id,
            InitialState = new ScenarioInitialState
            {
                Mode = "S",
                Ime = "off",
                Layers = [],
                Modifiers = []
            },
            Input = input.ToList(),
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "layer-gesture", "alt", "lifecycle"],
            RequiredEnvironment = ["hosted-windows"],
            OracleTargets = ["compiled-exe", "ahk-source", "ikeyd-runtime"]
        };

    private static void Compare(
        string scenarioId,
        string label,
        ScenarioRunResult expected,
        ScenarioRunResult actual,
        ICollection<string> failures)
    {
        var expectedEvents = CanonicalEvents(expected.Events);
        var actualEvents = CanonicalEvents(actual.Events);
        if (!expectedEvents.SequenceEqual(actualEvents, StringComparer.Ordinal))
        {
            failures.Add(
                $"{scenarioId} [{label}] expected [{string.Join(", ", expectedEvents)}], " +
                $"actual [{string.Join(", ", actualEvents)}].");
        }
    }

    private static ScenarioInputEvent Down(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };

    private static string[] CanonicalEvents(IReadOnlyList<ObservedKeyEvent> events)
        => events.Select(item => $"{item.Kind}:{CanonicalKey(item.Key)}").ToArray();

    private static string CanonicalKey(string key)
        => key.ToUpperInvariant() switch
        {
            "VK_A0" or "VK_A1" or "SHIFT" => "Shift",
            "VK_A2" or "VK_A3" or "CONTROL" or "CTRL" => "Control",
            "VK_A4" or "VK_A5" or "ALT" => "Alt",
            "VK_1C" or "CONVERT" or "HENKAN" => "Convert",
            "VK_1D" or "NONCONVERT" or "MUHENKAN" => "NonConvert",
            _ => key
        };
}
