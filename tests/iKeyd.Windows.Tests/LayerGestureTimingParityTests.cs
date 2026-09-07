using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LayerGestureTimingParityTests
{
    public static TheoryData<int, int, int> TimingProfiles => new()
    {
        // gap Space-down -> NonConvert-down, overlap/second-key hold, gap between releases (ms)
        { 1, 1, 1 },
        { 5, 5, 5 },
        { 10, 10, 10 },
        { 39, 1, 1 },
        { 40, 1, 1 },
        { 41, 1, 1 },
        { 1, 39, 1 },
        { 1, 40, 1 },
        { 1, 41, 1 },
        { 10, 100, 10 },
        { 100, 10, 100 },
        { 500, 10, 10 },
        { 10, 500, 10 },
        { 10, 10, 500 },
    };

    [Theory]
    [MemberData(nameof(TimingProfiles))]
    public async Task Space_then_NonConvert_release_NonConvert_first_is_ShiftEnter_for_timing_sweep(
        int pressGapMs,
        int overlapMs,
        int releaseGapMs)
    {
        var scenario = BuildScenario(
            $"timing-sm-m-first-{pressGapMs}-{overlapMs}-{releaseGapMs}",
            pressGapMs,
            overlapMs,
            releaseGapMs,
            releaseNonConvertFirst: true);

        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);

        Assert.Equal(
            new[] { "keyDown:Shift", "keyDown:Enter", "keyUp:Enter", "keyUp:Shift" },
            CanonicalEvents(actual.Events));
    }

    [Theory]
    [MemberData(nameof(TimingProfiles))]
    public async Task Space_then_NonConvert_release_Space_first_has_no_output_for_timing_sweep(
        int pressGapMs,
        int overlapMs,
        int releaseGapMs)
    {
        var scenario = BuildScenario(
            $"timing-sm-s-first-{pressGapMs}-{overlapMs}-{releaseGapMs}",
            pressGapMs,
            overlapMs,
            releaseGapMs,
            releaseNonConvertFirst: false);

        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);

        Assert.Empty(CanonicalEvents(actual.Events));
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task Space_NonConvert_release_order_timing_sweep_matches_both_pinned_legacy_oracles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiled = new LegacyLayerGestureScenarioRunner();
        var source = new HostedAutoHotkeySourceRunner(() => new LegacyLayerGestureScenarioRunner());
        if (!compiled.IsAvailable || !source.IsAvailable)
            return;

        var ikeyd = new IKeydRuntimeScenarioRunner();
        var failures = new List<string>();

        foreach (var profile in EnumerateTimingProfiles())
        {
            foreach (var releaseNonConvertFirst in new[] { true, false })
            {
                var scenario = BuildScenario(
                    $"timing-sm-{(releaseNonConvertFirst ? "m-first" : "s-first")}-{profile.PressGapMs}-{profile.OverlapMs}-{profile.ReleaseGapMs}",
                    profile.PressGapMs,
                    profile.OverlapMs,
                    profile.ReleaseGapMs,
                    releaseNonConvertFirst);

                try
                {
                    var current = await ikeyd.RunAsync(scenario);
                    var exe = await compiled.RunAsync(scenario);
                    var ahk = await source.RunAsync(scenario);

                    Compare(scenario.Id, "compiled EXE", current, exe, failures);
                    Compare(scenario.Id, "AHK source", current, ahk, failures);
                    Compare(scenario.Id, "legacy EXE vs AHK source", exe, ahk, failures);
                }
                catch (Exception error)
                {
                    failures.Add($"{scenario.Id}: {error.GetType().Name}: {error.Message}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            failures.Count == 0
                ? string.Empty
                : $"{failures.Count} timing-dependent layer-gesture mismatches:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<(int PressGapMs, int OverlapMs, int ReleaseGapMs)> EnumerateTimingProfiles()
    {
        foreach (var row in TimingProfiles)
            yield return ((int)row[0]!, (int)row[1]!, (int)row[2]!);
    }

    private static CompatibilityScenario BuildScenario(
        string id,
        int pressGapMs,
        int overlapMs,
        int releaseGapMs,
        bool releaseNonConvertFirst)
    {
        const long startMs = 10;
        var secondDownMs = startMs + pressGapMs;
        var firstUpMs = secondDownMs + overlapMs;
        var secondUpMs = firstUpMs + releaseGapMs;

        var input = new List<ScenarioInputEvent>
        {
            Down("SPACE", startMs),
            Down("NONCONVERT", secondDownMs),
        };

        if (releaseNonConvertFirst)
        {
            input.Add(Up("NONCONVERT", firstUpMs));
            input.Add(Up("SPACE", secondUpMs));
        }
        else
        {
            input.Add(Up("SPACE", firstUpMs));
            input.Add(Up("NONCONVERT", secondUpMs));
        }

        return new CompatibilityScenario
        {
            Id = id,
            InitialState = new ScenarioInitialState
            {
                Mode = "S",
                Ime = "off",
                Layers = [],
                Modifiers = []
            },
            Input = input,
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "layer-gesture", "timing"],
            RequiredEnvironment = ["hosted-windows"],
            OracleTargets = ["compiled-exe", "ahk-source", "ikeyd-runtime"]
        };
    }

    private static ScenarioInputEvent Down(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };

    private static void Compare(
        string scenarioId,
        string oracle,
        ScenarioRunResult expected,
        ScenarioRunResult actual,
        ICollection<string> failures)
    {
        var expectedEvents = CanonicalEvents(expected.Events);
        var actualEvents = CanonicalEvents(actual.Events);
        if (!expectedEvents.SequenceEqual(actualEvents, StringComparer.Ordinal))
        {
            failures.Add(
                $"{scenarioId} [{oracle}] events expected [{string.Join(", ", expectedEvents)}], " +
                $"actual [{string.Join(", ", actualEvents)}].");
        }
    }

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
