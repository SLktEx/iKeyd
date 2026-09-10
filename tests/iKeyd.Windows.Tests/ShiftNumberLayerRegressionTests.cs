using iKeyd.App;
using iKeyd.Compatibility.Tests;
using iKeyd.Core.Chords;
using iKeyd.Profiles.HotkeySkg.Layers;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class ShiftNumberLayerRegressionTests
{
    public static TheoryData<KeyCode, string> PinnedShiftNumberValues => new()
    {
        { KeyCode.Q, "#1" },
        { KeyCode.W, "#2" },
        { KeyCode.E, "#3" },
        { KeyCode.R, "#4" },
        { KeyCode.T, "#5" },
        { KeyCode.Y, "#6" },
        { KeyCode.U, "#7" },
        { KeyCode.I, "#8" },
        { KeyCode.O, "#9" },
        { KeyCode.P, "#0" },
        { KeyCode.At, "{F11}" },
        { KeyCode.A, "1" },
        { KeyCode.S, "2" },
        { KeyCode.D, "3" },
        { KeyCode.F, "4" },
        { KeyCode.G, "5" },
        { KeyCode.H, "6" },
        { KeyCode.J, "7" },
        { KeyCode.K, "8" },
        { KeyCode.L, "9" },
        { KeyCode.SColon, "0" },
        { KeyCode.Colon, "{F12}" },
        { KeyCode.Z, "{F1}" },
        { KeyCode.X, "{F2}" },
        { KeyCode.C, "{F3}" },
        { KeyCode.V, "{F4}" },
        { KeyCode.B, "{F5}" },
        { KeyCode.N, "{F6}" },
        { KeyCode.M, "{F7}" },
        { KeyCode.Comma, "{F8}" },
        { KeyCode.Dot, "{F9}" },
        { KeyCode.Slash, "{F10}" },
        { KeyCode.Digit1, "{F1}" },
        { KeyCode.Digit2, "{F2}" },
        { KeyCode.Digit3, "{F3}" },
        { KeyCode.Digit4, "{F4}" },
        { KeyCode.Digit5, "{F5}" },
        { KeyCode.Digit6, "{F6}" },
        { KeyCode.Digit7, "{F7}" },
        { KeyCode.Digit8, "{F8}" },
        { KeyCode.Digit9, "{F9}" },
        { KeyCode.Digit0, "{F10}" },
    };

    [Theory]
    [MemberData(nameof(PinnedShiftNumberValues))]
    public void Shift_number_table_matches_the_pinned_hotkeySKG_source(KeyCode key, string expected)
    {
        Assert.True(LegacyFunctionSendMap.TryGetShiftNumberValue(key, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SH_KSH_and_ASH_preserve_the_legacy_ordered_layer_semantics()
    {
        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.A,
            LayerState.FromSequence(LayerKey.S, LayerKey.H),
            out var sh));
        Assert.Equal("1", sh);

        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.A,
            LayerState.FromSequence(LayerKey.K, LayerKey.S, LayerKey.H),
            out var ksh));
        Assert.Equal("^1", ksh);

        Assert.True(LegacyFunctionSendMap.TryResolve(
            KeyCode.A,
            LayerState.FromSequence(LayerKey.A, LayerKey.S, LayerKey.H),
            out var ash));
        Assert.Equal("!1", ash);

        Assert.False(LegacyFunctionSendMap.TryResolve(
            KeyCode.A,
            LayerState.FromSequence(LayerKey.H, LayerKey.S),
            out _));
    }

    [Fact]
    public void SH_hot_path_lookup_allocates_nothing_after_static_initialization()
    {
        const int MeasurementIterations = 10_000;
        const int MaxWarmupWindows = 6;
        const int RequiredStableWindows = 2;

        var state = LayerState.FromSequence(LayerKey.S, LayerKey.H);
        Assert.True(LegacyFunctionSendMap.TryResolve(KeyCode.A, state, out _));

        var stableWindows = 0;
        long lastAllocated = long.MaxValue;
        string? last = null;
        var allResolved = true;
        for (var attempt = 0; attempt < MaxWarmupWindows; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < MeasurementIterations; i++)
            {
                allResolved &= LegacyFunctionSendMap.TryResolve(KeyCode.A, state, out var output);
                last = output;
            }
            lastAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

            stableWindows = lastAllocated == 0 ? stableWindows + 1 : 0;
            if (stableWindows >= RequiredStableWindows)
                break;
        }

        GC.KeepAlive(last);
        Assert.True(allResolved);
        Assert.True(
            stableWindows >= RequiredStableWindows,
            $"SH lookup did not reach steady-state zero allocation; last window allocated {lastAllocated} bytes.");
    }

    [Fact]
    public async Task Physical_Space_then_Convert_then_A_emits_1_instead_of_passing_A_through()
    {
        var scenario = new CompatibilityScenario
        {
            Id = "v07-sh-number-layer-a-1",
            InitialState = new ScenarioInitialState
            {
                Mode = "R",
                Ime = "off"
            },
            Input =
            [
                new ScenarioInputEvent { AtMs = 0, Kind = "keyDown", Key = "Space" },
                new ScenarioInputEvent { AtMs = 10, Kind = "keyDown", Key = "Convert" },
                new ScenarioInputEvent { AtMs = 20, Kind = "keyDown", Key = "A" },
                new ScenarioInputEvent { AtMs = 21, Kind = "keyUp", Key = "A" },
                new ScenarioInputEvent { AtMs = 30, Kind = "keyUp", Key = "Convert" },
                new ScenarioInputEvent { AtMs = 40, Kind = "keyUp", Key = "Space" }
            ],
            Expected = new ScenarioExpected
            {
                Text = "1"
            }
        };

        var result = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);
        var differences = CompatibilityScenarioDiff.Compare(scenario, result);

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }
}
