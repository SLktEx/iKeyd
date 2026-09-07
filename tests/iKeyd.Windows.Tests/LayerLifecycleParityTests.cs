using System.Text.Json;
using iKeyd.Compatibility.Tests;
using iKeyd.Profiles.HotkeySkg.Layers;
using Xunit;

namespace iKeyd.Windows.Tests;

[Collection(GlobalWindowsInputCollection.Name)]
public sealed class LayerLifecycleParityTests
{
    private static string RuntimeFixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");

    // #231 already exercises these transitions through complete physical M/H/S
    // gestures (plus the KMS release branch) against both legacy oracles.
    private static readonly string[] CoveredByExistingGestureAudit =
    [
        "m-down",
        "h-down",
        "space-down",
        "m-then-h",
        "h-then-m",
        "mh-h-up-tap",
        "hm-m-up-tap",
        "h-up-tap",
        "s-up-tap",
        "ms-space-up",
        "hs-space-up",
        "kms-space-up",
    ];

    private static readonly PhysicalCase[] PhysicalCases =
    [
        new(
            "kana-toggle-on-kh-release",
            ["kana-empty"],
            [Down("KANA", 10), Up("KANA", 20), Down("CONVERT", 30), Up("CONVERT", 40)],
            ["keyDown:Convert", "keyUp:Convert"],
            HostedLegacy: true),

        new(
            "kana-toggle-off-h-tap",
            ["kana-k-toggle-off"],
            [
                Down("KANA", 10), Up("KANA", 20),
                Down("KANA", 30), Up("KANA", 40),
                Down("CONVERT", 50), Up("CONVERT", 60)
            ],
            ["keyDown:Control", "keyUp:Control"],
            HostedLegacy: true),

        new(
            "kana-while-m",
            ["kana-m"],
            [Down("NONCONVERT", 10), Down("KANA", 20), Up("KANA", 30), Up("NONCONVERT", 40)],
            ["keyDown:NonConvert", "keyUp:NonConvert"],
            HostedLegacy: true),

        new(
            "kana-while-h",
            ["kana-h"],
            [Down("CONVERT", 10), Down("KANA", 20), Up("KANA", 30), Up("CONVERT", 40)],
            ["keyDown:Convert", "keyUp:Convert"],
            HostedLegacy: true),

        new(
            "kana-while-s",
            ["kana-s"],
            [Down("SPACE", 10), Down("KANA", 20), Up("KANA", 30), Up("SPACE", 40)],
            ["keyDown:Control", "keyDown:Escape", "keyUp:Escape", "keyUp:Control"],
            HostedLegacy: true),

        new(
            "k-space-release-clears-k",
            ["ks-space-up"],
            [
                Down("KANA", 10), Up("KANA", 20),
                Down("SPACE", 30), Up("SPACE", 40),
                Down("CONVERT", 50), Up("CONVERT", 60)
            ],
            [
                "keyDown:Shift", "keyDown:Space", "keyUp:Space", "keyUp:Shift",
                "keyDown:Control", "keyUp:Control"
            ],
            HostedLegacy: true),

        new(
            "a-m-space-release-clears-a",
            ["ams-space-up"],
            [
                Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40),
                Down("NONCONVERT", 50), Down("SPACE", 60), Up("SPACE", 70), Up("NONCONVERT", 80),
                Down("CONVERT", 90), Up("CONVERT", 100)
            ],
            [
                "keyDown:Control", "keyUp:Control",
                "keyDown:Alt", "keyDown:Enter", "keyUp:Enter", "keyUp:Alt",
                "keyDown:Control", "keyUp:Control"
            ]),

        new(
            "alt-kana-a-is-one-shot",
            ["alt-kana"],
            [
                Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40),
                Down("Q", 50), Up("Q", 60),
                Down("CONVERT", 70), Up("CONVERT", 80)
            ],
            [
                "keyDown:Control", "keyUp:Control",
                "keyDown:Alt", "keyDown:Q", "keyUp:Q", "keyUp:Alt",
                "keyDown:Control", "keyUp:Control"
            ]),

        new(
            "alt-convert-press-release-cleans-ah",
            ["alt-h-down", "alt-h-up"],
            [
                Down("ALT", 10), Down("CONVERT", 20), Up("CONVERT", 30), Up("ALT", 40),
                Down("CONVERT", 50), Up("CONVERT", 60)
            ],
            [
                "keyDown:Control", "keyUp:Control",
                "keyDown:Control", "keyUp:Control"
            ]),

        new(
            "m-alt-space-does-not-become-ams",
            ["alt-space-down"],
            [
                Down("NONCONVERT", 10),
                Down("ALT", 20), Down("SPACE", 30), Up("SPACE", 40), Up("ALT", 50),
                Up("NONCONVERT", 60),
                Down("CONVERT", 70), Up("CONVERT", 80)
            ],
            [
                "keyDown:Control", "keyUp:Control",
                "keyDown:Control", "keyUp:Control"
            ]),

        new(
            "a-m-alt-space-release-emits-alt-space-and-cleans-a",
            ["alt-space-up-ams"],
            [
                Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40),
                Down("NONCONVERT", 50),
                Down("ALT", 60), Down("SPACE", 70), Up("SPACE", 80), Up("ALT", 90),
                Up("NONCONVERT", 100),
                Down("CONVERT", 110), Up("CONVERT", 120)
            ],
            [
                "keyDown:Control", "keyUp:Control",
                "keyDown:Control", "keyUp:Control",
                "keyDown:Alt", "keyDown:Space", "keyUp:Space", "keyUp:Alt",
                "keyDown:Control", "keyUp:Control"
            ]),

        // `K` is documented as one-shot for ordinary modified dispatch.  #230
        // verifies Win+key output; this follow-up additionally proves the sticky
        // K state is actually gone before the next H tap.
        new(
            "k-modified-dispatch-is-one-shot",
            [],
            [
                Down("KANA", 10), Up("KANA", 20),
                Down("Q", 30), Up("Q", 40),
                Down("CONVERT", 50), Up("CONVERT", 60)
            ],
            [
                "keyDown:LWin", "keyDown:Q", "keyUp:Q", "keyUp:LWin",
                "keyDown:Control", "keyUp:Control"
            ])
    ];

    public static IEnumerable<object[]> RuntimePhysicalCases()
        => PhysicalCases.Select(item => new object[] { item });

    [Fact]
    public void Every_pinned_layer_case_matches_the_executable_state_machine()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));

        foreach (var item in document.RootElement.GetProperty("layerCases").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? string.Empty;
            var initial = ParseState(item.GetProperty("initialState").GetString() ?? string.Empty);
            var initialConsumed = item.GetProperty("initialFlag").GetInt32() != 0;
            var layerEvent = Enum.Parse<LayerEvent>(item.GetProperty("event").GetString() ?? string.Empty);

            var transition = LayerStateMachine.Apply(new LayerRuntimeState(initial, initialConsumed), layerEvent);

            Assert.True(
                string.Equals(item.GetProperty("finalState").GetString(), transition.State.Layers.ToString(), StringComparison.Ordinal),
                $"{name}: final state expected '{item.GetProperty("finalState").GetString()}', actual '{transition.State.Layers}'.");
            Assert.Equal(item.GetProperty("finalFlag").GetInt32() != 0, transition.State.Consumed);

            // The pinned fixture captures hotkeySKG's logical layer actions. AHK
            // itself additionally emits a Ctrl tap for physical Alt hook hotkeys
            // to mask menu activation. Keep that transport artifact out of the
            // fixture while still requiring the executable state machine to
            // reproduce it for the three Alt-down trigger events.
            var expectedActions = layerEvent is LayerEvent.AltHDown or LayerEvent.AltSpaceDown or LayerEvent.AltKanaDown
                ? new[] { "Ctrl" }
                : item.GetProperty("actions").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();

            Assert.Equal(expectedActions, transition.Actions.Select(ActionName).ToArray());
        }
    }

    [Fact]
    public void Every_pinned_layer_case_has_a_physical_runtime_coverage_owner()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RuntimeFixturePath));
        var fixtureNames = document.RootElement.GetProperty("layerCases")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString() ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var covered = CoveredByExistingGestureAudit
            .Concat(PhysicalCases.SelectMany(item => item.FixtureNames))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(fixtureNames, covered);
    }

    [Theory]
    [MemberData(nameof(RuntimePhysicalCases))]
    public async Task Sticky_and_alt_layer_physical_routes_match_pinned_semantics(PhysicalCase testCase)
    {
        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(Scenario(testCase.Id, testCase.Input));

        Assert.Null(actual.Text);
        Assert.Empty(actual.Actions);
        Assert.Equal(testCase.ExpectedEvents, CanonicalEvents(actual.Events));
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task Kana_and_K_release_lifecycle_matches_both_pinned_legacy_oracles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var scenarios = PhysicalCases
            .Where(item => item.HostedLegacy)
            .Select(item => Scenario(item.Id, item.Input))
            .ToArray();

        var compiled = new LegacyLayerGestureScenarioRunner();
        var source = new HostedAutoHotkeySourceRunner(() => new LegacyLayerGestureScenarioRunner());
        if (!compiled.IsAvailable || !source.IsAvailable)
            return;

        var runtime = new IKeydRuntimeScenarioRunner();
        var failures = new List<string>();

        foreach (var scenario in scenarios)
        {
            try
            {
                var current = await runtime.RunAsync(scenario);
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

        Assert.True(
            failures.Count == 0,
            failures.Count == 0
                ? string.Empty
                : $"{failures.Count} Kana/K lifecycle mismatches:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, failures));
    }

    private static CompatibilityScenario Scenario(string id, IReadOnlyList<ScenarioInputEvent> input)
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
            Tags = ["legacy", "layer-gesture", "lifecycle"],
            RequiredEnvironment = ["hosted-windows"],
            OracleTargets = ["compiled-exe", "ahk-source", "ikeyd-runtime"]
        };

    private static LayerState ParseState(string value)
        => LayerState.FromSequence(value.Select(LayerKeyFromCode).ToArray());

    private static LayerKey LayerKeyFromCode(char value)
        => value switch
        {
            'M' => LayerKey.M,
            'H' => LayerKey.H,
            'S' => LayerKey.S,
            'K' => LayerKey.K,
            'A' => LayerKey.A,
            _ => throw new InvalidDataException($"Unknown fixture layer '{value}'.")
        };

    private static string ActionName(LayerAction action)
        => action switch
        {
            LayerAction.Tab => "Tab",
            LayerAction.ShiftTab => "Shift+Tab",
            LayerAction.ShiftEnter => "Shift+Enter",
            LayerAction.ShiftSpace => "Shift+Space",
            LayerAction.Ctrl => "Ctrl",
            LayerAction.Space => "Space",
            LayerAction.Enter => "Enter",
            LayerAction.CtrlSpace => "Ctrl+Space",
            LayerAction.CtrlEnter => "Ctrl+Enter",
            LayerAction.AltEnter => "Alt+Enter",
            LayerAction.AltSpace => "Alt+Space",
            LayerAction.CtrlEsc => "Ctrl+Esc",
            LayerAction.Muhenkan => "Muhenkan",
            LayerAction.Henkan => "Henkan",
            LayerAction.EndEnter => "End+Enter",
            LayerAction.UpEndEnter => "Up+End+Enter",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

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
            "VK_5B" or "LWIN" => "LWin",
            _ => key
        };

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

    public sealed record PhysicalCase(
        string Id,
        string[] FixtureNames,
        ScenarioInputEvent[] Input,
        string[] ExpectedEvents,
        bool HostedLegacy = false);
}
