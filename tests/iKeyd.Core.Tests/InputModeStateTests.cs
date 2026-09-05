using System.Text.Json;
using iKeyd.Core.Input;
using iKeyd.Core.Modes;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class InputModeStateTests
{
    [Fact]
    public void S_R_and_K_switches_set_the_expected_keymap()
    {
        Assert.Equal(new InputModeState(InputMode.S, KeymapMode.S), InputModeState.Initial.SwitchTo(InputMode.S));
        Assert.Equal(new InputModeState(InputMode.R, null), InputModeState.Initial.SwitchTo(InputMode.R));
        Assert.Equal(new InputModeState(InputMode.K, KeymapMode.K), InputModeState.Initial.SwitchTo(InputMode.K));
    }

    [Fact]
    public void T_mode_preserves_the_previous_keymap_like_the_legacy_runtime()
    {
        Assert.Equal(KeymapMode.S, InputModeState.Initial.SwitchTo(InputMode.T).ActiveKeymap);
        Assert.Equal(KeymapMode.K, new InputModeState(InputMode.K, KeymapMode.K).SwitchTo(InputMode.T).ActiveKeymap);
        Assert.Null(new InputModeState(InputMode.R, null).SwitchTo(InputMode.T).ActiveKeymap);
    }

    [Fact]
    public void Routing_matches_all_legacy_fixture_cases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.runtime.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var item in document.RootElement.GetProperty("ime").GetProperty("routingCases").EnumerateArray())
        {
            var state = new InputModeState(
                ParseMode(item.GetProperty("gmode").GetString() ?? ""),
                ParseKeymap(item.GetProperty("gimode").GetString() ?? ""));
            var route = state.Route(item.GetProperty("imeRomaKana").GetBoolean());
            var expected = item.GetProperty("expected").GetString() ?? "";

            if (expected == "PassThrough")
            {
                Assert.Equal(InputRouteKind.PassThrough, route.Kind);
                Assert.Null(route.Keymap);
            }
            else
            {
                Assert.Equal(InputRouteKind.ChordEngine, route.Kind);
                Assert.Equal(expected["ChordEngine:".Length..], route.Keymap?.ToString() ?? "");
            }
        }
    }

    [Fact]
    public void Route_can_use_the_platform_neutral_input_method_interface()
    {
        var active = new FakeInputMethod(true);
        var inactive = new FakeInputMethod(false);

        Assert.Equal(InputRouteKind.ChordEngine, InputModeState.Initial.Route(active).Kind);
        Assert.Equal(InputRouteKind.PassThrough, InputModeState.Initial.Route(inactive).Kind);
    }

    private static InputMode ParseMode(string value) => value switch
    {
        "S" => InputMode.S,
        "R" => InputMode.R,
        "T" => InputMode.T,
        "K" => InputMode.K,
        _ => throw new InvalidDataException()
    };

    private static KeymapMode? ParseKeymap(string value) => value switch
    {
        "S" => KeymapMode.S,
        "K" => KeymapMode.K,
        "" => null,
        _ => throw new InvalidDataException()
    };

    private sealed class FakeInputMethod(bool active) : IInputMethod
    {
        public bool IsKanaInputActive() => active;
    }
}
