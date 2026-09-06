using System.Text.Json;
using iKeyd.Core.Layers;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class LayerStateMachineTests
{
    [Fact]
    public void Layer_state_is_typed_but_preserves_press_order()
    {
        var mh = LayerState.FromSequence(LayerKey.M, LayerKey.H);
        var hm = LayerState.FromSequence(LayerKey.H, LayerKey.M);
        Assert.Equal(LayerModifiers.M | LayerModifiers.H, mh.Modifiers);
        Assert.Equal(mh.Modifiers, hm.Modifiers);
        Assert.NotEqual(mh, hm);
        Assert.Equal("MH", mh.ToString());
        Assert.Equal("HM", hm.ToString());
    }

    [Fact]
    public void Press_is_idempotent_and_release_removes_typed_layers()
    {
        var state = LayerState.Empty.Press(LayerKey.M).Press(LayerKey.H).Press(LayerKey.M);
        Assert.Equal("MH", state.ToString());
        Assert.Equal("H", state.Release(LayerKey.M, LayerKey.K).ToString());
    }

    [Fact]
    public void Legacy_layer_fixture_cases_match_except_the_pinned_exe_kana_s_divergence()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var item in document.RootElement.GetProperty("layerCases").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? "unnamed";
            var initial = ParseState(item.GetProperty("initialState").GetString() ?? "");
            var consumed = item.GetProperty("initialFlag").GetInt32() != 0;
            var @event = Enum.Parse<LayerEvent>(item.GetProperty("event").GetString() ?? "", ignoreCase: false);
            var result = LayerStateMachine.Apply(new LayerRuntimeState(initial, consumed), @event);

            Assert.Equal(item.GetProperty("finalState").GetString(), result.State.Layers.ToString());
            if (name == "kana-s")
            {
                Assert.True(item.GetProperty("finalFlag").GetInt32() != 0); // original AHK source
                Assert.False(result.State.Consumed); // pinned compiled EXE target
            }
            else
            {
                Assert.Equal(item.GetProperty("finalFlag").GetInt32() != 0, result.State.Consumed);
            }

            var expectedActions = item.GetProperty("actions").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            var actualActions = result.Actions.Select(ToFixtureName).ToArray();
            Assert.True(expectedActions.SequenceEqual(actualActions), $"Action mismatch in fixture case '{name}'");
        }
    }

    [Fact]
    public void MarkConsumed_suppresses_tap_action_on_release()
    {
        var state = new LayerRuntimeState(LayerState.FromSequence(LayerKey.H), false).MarkConsumed();
        var result = LayerStateMachine.Apply(state, LayerEvent.HUp);
        Assert.Empty(result.Actions);
        Assert.Equal("", result.State.Layers.ToString());
    }

    private static LayerState ParseState(string state)
    {
        var result = LayerState.Empty;
        foreach (var ch in state)
        {
            result = result.Press(ch switch
            {
                'M' => LayerKey.M, 'H' => LayerKey.H, 'S' => LayerKey.S, 'K' => LayerKey.K, 'A' => LayerKey.A,
                _ => throw new InvalidDataException($"Unknown layer '{ch}'.")
            });
        }
        return result;
    }

    private static string ToFixtureName(LayerAction action) => action switch
    {
        LayerAction.Tab => "Tab", LayerAction.ShiftTab => "Shift+Tab", LayerAction.ShiftEnter => "Shift+Enter",
        LayerAction.ShiftSpace => "Shift+Space", LayerAction.Ctrl => "Ctrl", LayerAction.Space => "Space",
        LayerAction.Enter => "Enter", LayerAction.CtrlSpace => "Ctrl+Space", LayerAction.CtrlEnter => "Ctrl+Enter",
        LayerAction.AltEnter => "Alt+Enter", LayerAction.AltSpace => "Alt+Space", LayerAction.CtrlEsc => "Ctrl+Esc",
        LayerAction.Muhenkan => "Muhenkan", LayerAction.Henkan => "Henkan", LayerAction.EndEnter => "End+Enter",
        LayerAction.UpEndEnter => "Up+End+Enter", _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
