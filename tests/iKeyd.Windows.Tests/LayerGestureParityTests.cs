using iKeyd.Compatibility.Tests;
using iKeyd.Core.Layers;
using iKeyd.Profiles.HotkeySkg.Layers;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LayerGestureParityTests
{
    private static readonly char[] HeldLayers = ['M', 'H', 'S'];

    public static TheoryData<string, string, string[]> TwoLayerCases => new()
    {
        // Press order, release order, semantic output events.
        { "SM", "MS", ["keyDown:Shift", "keyDown:Enter", "keyUp:Enter", "keyUp:Shift"] },
        { "SM", "SM", [] },
        { "MS", "SM", ["keyDown:Enter", "keyUp:Enter"] },
        { "MS", "MS", ["keyDown:Space", "keyUp:Space"] },
        { "MH", "HM", ["keyDown:Tab", "keyUp:Tab"] },
        { "MH", "MH", ["keyDown:Control", "keyUp:Control"] },
        { "HM", "MH", ["keyDown:Shift", "keyDown:Tab", "keyUp:Tab", "keyUp:Shift"] },
        { "HM", "HM", [] },
        { "HS", "SH", ["keyDown:Control", "keyDown:Space", "keyUp:Space", "keyUp:Control"] },
        { "HS", "HS", ["keyDown:Space", "keyUp:Space"] },
        { "SH", "HS", ["keyDown:Space", "keyUp:Space"] },
        { "SH", "SH", ["keyDown:Control", "keyUp:Control"] },
    };

    [Theory]
    [MemberData(nameof(TwoLayerCases))]
    public async Task Every_two_layer_press_release_order_has_pinned_semantic_output(
        string pressOrder,
        string releaseOrder,
        string[] expectedEvents)
    {
        var scenario = BuildHeldLayerGesture($"runtime-{pressOrder}-{releaseOrder}", pressOrder, releaseOrder);
        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);

        Assert.Null(actual.Text);
        Assert.Empty(actual.Actions);
        Assert.Equal(expectedEvents, CanonicalEvents(actual.Events));
    }

    [Fact]
    public void Every_MHS_press_release_permutation_returns_to_empty_state()
    {
        foreach (var pressOrder in Permutations("MHS"))
        {
            foreach (var releaseOrder in Permutations("MHS"))
            {
                var state = LayerRuntimeState.Empty;
                foreach (var layer in pressOrder)
                    state = LayerStateMachine.Apply(state, DownEvent(layer)).State;
                foreach (var layer in releaseOrder)
                    state = LayerStateMachine.Apply(state, UpEvent(layer)).State;

                Assert.True(
                    state.Layers.Count == 0,
                    $"Press {pressOrder}, release {releaseOrder} left layer state '{state.Layers}'.");
            }
        }
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task All_two_layer_press_release_orders_match_both_pinned_legacy_oracles()
    {
        var scenarios = new List<CompatibilityScenario>();
        foreach (var first in HeldLayers)
        {
            foreach (var second in HeldLayers)
            {
                if (second == first)
                    continue;

                var press = $"{first}{second}";
                scenarios.Add(BuildHeldLayerGesture($"layer-two-{press}-fifo", press, press));
                scenarios.Add(BuildHeldLayerGesture($"layer-two-{press}-lifo", press, new string([second, first])));
            }
        }

        await AssertMatchesBothOracles(scenarios);
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task All_MHS_press_release_permutations_match_both_pinned_legacy_oracles()
    {
        var scenarios = new List<CompatibilityScenario>();
        foreach (var press in Permutations("MHS"))
        {
            foreach (var release in Permutations("MHS"))
                scenarios.Add(BuildHeldLayerGesture($"layer-three-{press}-{release}", press, release));
        }

        await AssertMatchesBothOracles(scenarios);
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task Sticky_and_immediate_action_branches_match_both_pinned_legacy_oracles()
    {
        var scenarios = new[]
        {
            // K + H, then H-up: Henkan/Convert branch.
            Scenario("layer-kh-hup",
                Down("KANA", 10), Up("KANA", 20),
                Down("CONVERT", 30), Up("CONVERT", 40)),

            // K + M + S, release Space first: Ctrl+Enter branch.
            Scenario("layer-kms-sup",
                Down("KANA", 10), Up("KANA", 20),
                Down("NONCONVERT", 30), Down("SPACE", 40),
                Up("SPACE", 50), Up("NONCONVERT", 60)),

            // M + S + H: H-down emits Shift+Space immediately.
            Scenario("layer-msh-hdown",
                Down("NONCONVERT", 10), Down("SPACE", 20), Down("CONVERT", 30),
                Up("CONVERT", 40), Up("SPACE", 50), Up("NONCONVERT", 60)),

            // M + H + S: Space-down emits End+Enter immediately.
            Scenario("layer-mhs-sdown",
                Down("NONCONVERT", 10), Down("CONVERT", 20), Down("SPACE", 30),
                Up("SPACE", 40), Up("CONVERT", 50), Up("NONCONVERT", 60)),

            // H + M + S: Space-down emits Up+End+Enter immediately.
            Scenario("layer-hms-sdown",
                Down("CONVERT", 10), Down("NONCONVERT", 20), Down("SPACE", 30),
                Up("SPACE", 40), Up("NONCONVERT", 50), Up("CONVERT", 60)),
        };

        await AssertMatchesBothOracles(scenarios);
    }

    private static async Task AssertMatchesBothOracles(IReadOnlyList<CompatibilityScenario> scenarios)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiled = new LegacyLayerGestureScenarioRunner();
        var source = new HostedAutoHotkeySourceRunner(() => new LegacyLayerGestureScenarioRunner());
        if (!compiled.IsAvailable || !source.IsAvailable)
            return;

        var ikeydRunner = new IKeydRuntimeScenarioRunner();
        var failures = new List<string>();

        foreach (var scenario in scenarios)
        {
            try
            {
                var ikeyd = await ikeydRunner.RunAsync(scenario);
                var exe = await compiled.RunAsync(scenario);
                var ahk = await source.RunAsync(scenario);

                Compare(scenario.Id, "compiled EXE", ikeyd, exe, failures);
                Compare(scenario.Id, "AHK source", ikeyd, ahk, failures);
                Compare(scenario.Id, "legacy EXE vs AHK source", exe, ahk, failures);
            }
            catch (Exception error)
            {
                failures.Add($"{scenario.Id}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            failures.Count == 0
                ? string.Empty
                : $"{failures.Count} layer-gesture mismatches:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, failures));
    }

    private static void Compare(
        string scenarioId,
        string oracle,
        ScenarioRunResult expected,
        ScenarioRunResult actual,
        ICollection<string> failures)
    {
        if (!string.Equals(expected.Text, actual.Text, StringComparison.Ordinal))
            failures.Add($"{scenarioId} [{oracle}] text expected '{expected.Text}', actual '{actual.Text}'.");

        var expectedEvents = CanonicalEvents(expected.Events);
        var actualEvents = CanonicalEvents(actual.Events);
        if (!expectedEvents.SequenceEqual(actualEvents, StringComparer.Ordinal))
        {
            failures.Add(
                $"{scenarioId} [{oracle}] events expected [{string.Join(", ", expectedEvents)}], " +
                $"actual [{string.Join(", ", actualEvents)}].");
        }

        var expectedActions = expected.Actions.Select(action => $"{action.Kind}:{action.Value}").ToArray();
        var actualActions = actual.Actions.Select(action => $"{action.Kind}:{action.Value}").ToArray();
        if (!expectedActions.SequenceEqual(actualActions, StringComparer.Ordinal))
        {
            failures.Add(
                $"{scenarioId} [{oracle}] actions expected [{string.Join(", ", expectedActions)}], " +
                $"actual [{string.Join(", ", actualActions)}].");
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

    private static CompatibilityScenario BuildHeldLayerGesture(string id, string pressOrder, string releaseOrder)
    {
        var input = new List<ScenarioInputEvent>();
        long at = 10;
        foreach (var layer in pressOrder)
        {
            input.Add(Down(KeyName(layer), at));
            at += 10;
        }
        foreach (var layer in releaseOrder)
        {
            input.Add(Up(KeyName(layer), at));
            at += 10;
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
            Tags = ["legacy", "layer-gesture"],
            RequiredEnvironment = ["hosted-windows"],
            OracleTargets = ["compiled-exe", "ahk-source", "ikeyd-runtime"]
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

    private static LayerEvent DownEvent(char layer)
        => layer switch
        {
            'M' => LayerEvent.MDown,
            'H' => LayerEvent.HDown,
            'S' => LayerEvent.SpaceDown,
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };

    private static LayerEvent UpEvent(char layer)
        => layer switch
        {
            'M' => LayerEvent.MUp,
            'H' => LayerEvent.HUp,
            'S' => LayerEvent.SpaceUp,
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };

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
