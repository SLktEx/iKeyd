using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

[Collection(GlobalWindowsInputCollection.Name)]
public sealed class AltLayerLegacyDifferentialTests
{
    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task Physical_AltSpace_from_AM_matches_both_pinned_legacy_oracles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiled = new LegacyAltLayerGestureScenarioRunner();
        var source = new HostedAutoHotkeySourceRunner(() => new LegacyAltLayerGestureScenarioRunner());
        if (!compiled.IsAvailable || !source.IsAvailable)
            return;

        var scenario = new CompatibilityScenario
        {
            Id = "physical-alt-space-from-am",
            InitialState = new ScenarioInitialState
            {
                Mode = "S",
                Ime = "off",
                Layers = [],
                Modifiers = []
            },
            Input =
            [
                Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40),
                Down("NONCONVERT", 50),
                Down("ALT", 60), Down("SPACE", 70), Up("SPACE", 80), Up("ALT", 90),
                Up("NONCONVERT", 100),
                Down("CONVERT", 110), Up("CONVERT", 120)
            ],
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "layer-gesture", "alt", "lifecycle"],
            RequiredEnvironment = ["hosted-windows"],
            OracleTargets = ["compiled-exe", "ahk-source", "ikeyd-runtime"]
        };

        var runtime = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);
        var exe = await compiled.RunAsync(scenario);
        var ahk = await source.RunAsync(scenario);

        var exeEvents = CanonicalEvents(exe.Events);
        var ahkEvents = CanonicalEvents(ahk.Events);
        var runtimeEvents = CanonicalEvents(runtime.Events);

        Assert.Equal(exeEvents, ahkEvents);
        Assert.Equal(exeEvents, runtimeEvents);
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
