using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LayerGestureTimestampInvarianceTests
{
    private static readonly int[] UniformGapsMs = [1, 5, 39, 40, 41, 100, 500];

    [Fact]
    public async Task Every_two_layer_MHS_gesture_is_invariant_across_independent_timing_phases()
    {
        var runner = new IKeydRuntimeScenarioRunner();

        foreach (var pressOrder in OrderedPairs("MHS"))
        {
            foreach (var releaseOrder in Permutations(pressOrder))
            {
                var baseline = await runner.RunAsync(BuildTwoLayerScenario(
                    $"baseline-{pressOrder}-{releaseOrder}",
                    pressOrder,
                    releaseOrder,
                    10,
                    10,
                    10));
                var expected = Signature(baseline);

                foreach (var profile in LayerGestureTimingProfiles.All)
                {
                    var actual = await runner.RunAsync(BuildTwoLayerScenario(
                        $"timed-{pressOrder}-{releaseOrder}-{profile.PressGapMs}-{profile.OverlapMs}-{profile.ReleaseGapMs}",
                        pressOrder,
                        releaseOrder,
                        profile.PressGapMs,
                        profile.OverlapMs,
                        profile.ReleaseGapMs));

                    Assert.Equal(expected, Signature(actual));
                }
            }
        }
    }

    [Fact]
    public async Task Every_three_layer_MHS_press_release_order_is_invariant_across_timestamp_scale()
    {
        var runner = new IKeydRuntimeScenarioRunner();

        foreach (var pressOrder in Permutations("MHS"))
        {
            foreach (var releaseOrder in Permutations("MHS"))
            {
                var baseline = await runner.RunAsync(BuildUniformScenario(
                    $"baseline-{pressOrder}-{releaseOrder}",
                    pressOrder,
                    releaseOrder,
                    10));
                var expected = Signature(baseline);

                foreach (var gapMs in UniformGapsMs)
                {
                    var actual = await runner.RunAsync(BuildUniformScenario(
                        $"timed-{pressOrder}-{releaseOrder}-{gapMs}",
                        pressOrder,
                        releaseOrder,
                        gapMs));

                    Assert.Equal(expected, Signature(actual));
                }
            }
        }
    }

    private static CompatibilityScenario BuildTwoLayerScenario(
        string id,
        string pressOrder,
        string releaseOrder,
        int pressGapMs,
        int overlapMs,
        int releaseGapMs)
    {
        const long startMs = 10;
        var secondDownMs = startMs + pressGapMs;
        var firstUpMs = secondDownMs + overlapMs;
        var finalUpMs = firstUpMs + releaseGapMs;

        return Scenario(
            id,
            Down(KeyName(pressOrder[0]), startMs),
            Down(KeyName(pressOrder[1]), secondDownMs),
            Up(KeyName(releaseOrder[0]), firstUpMs),
            Up(KeyName(releaseOrder[1]), finalUpMs));
    }

    private static CompatibilityScenario BuildUniformScenario(
        string id,
        string pressOrder,
        string releaseOrder,
        int gapMs)
    {
        var input = new List<ScenarioInputEvent>();
        long atMs = 10;

        foreach (var layer in pressOrder)
        {
            input.Add(Down(KeyName(layer), atMs));
            atMs += gapMs;
        }

        foreach (var layer in releaseOrder)
        {
            input.Add(Up(KeyName(layer), atMs));
            atMs += gapMs;
        }

        return Scenario(id, input.ToArray());
    }

    private static CompatibilityScenario Scenario(string id, params ScenarioInputEvent[] input)
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
            Tags = ["layer-gesture", "timestamp-invariance"],
            RequiredEnvironment = ["windows-runtime"],
            OracleTargets = ["ikeyd-runtime"]
        };

    private static ScenarioInputEvent Down(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };

    private static string KeyName(char layer)
        => layer switch
        {
            'M' => "NONCONVERT",
            'H' => "CONVERT",
            'S' => "SPACE",
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };

    private static string Signature(ScenarioRunResult result)
    {
        var events = string.Join(",", result.Events.Select(item => $"{item.Kind}:{CanonicalKey(item.Key)}"));
        var actions = string.Join(",", result.Actions.Select(item => $"{item.Kind}:{item.Value}"));
        return $"text={result.Text ?? "<null>"}|events={events}|actions={actions}";
    }

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

    private static IEnumerable<string> OrderedPairs(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            for (var j = 0; j < value.Length; j++)
            {
                if (i != j)
                    yield return new string([value[i], value[j]]);
            }
        }
    }

    private static IEnumerable<string> Permutations(string value)
    {
        if (value.Length <= 1)
        {
            yield return value;
            yield break;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var head = value[index];
            var tail = value.Remove(index, 1);
            foreach (var permutation in Permutations(tail))
                yield return head + permutation;
        }
    }
}
