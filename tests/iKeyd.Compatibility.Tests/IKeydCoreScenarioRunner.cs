using System.Text.Json;
using iKeyd.Core.Chords;
using iKeyd.Core.Keymaps;

namespace iKeyd.Compatibility.Tests;

public sealed class IKeydCoreScenarioRunner : ICompatibilityScenarioRunner
{
    public string Name => "iKeyd.Core";
    public bool IsAvailable => true;

    public Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        cancellationToken.ThrowIfCancellationRequested();

        if (scenario.InitialState.Modifiers.Count != 0)
            throw new NotSupportedException("The core chord runner does not apply modifier state yet.");

        var engine = new ChordEngine<string>(LegacyKeymapLoader.Load(scenario.InitialState.Mode));
        var output = new List<string>();

        foreach (var input in scenario.Input)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Model the event loop deterministically: expire pending input before
            // processing a new event, except that the inclusive 40ms boundary is
            // intentionally retained by ChordEngine.AdvanceTo.
            output.AddRange(engine.AdvanceTo(input.AtMs));

            if (string.Equals(input.Kind, "keyDown", StringComparison.OrdinalIgnoreCase))
                output.AddRange(engine.OnKeyDown(input.Key!, input.AtMs));
        }

        output.AddRange(engine.Flush());

        return Task.FromResult(new ScenarioRunResult
        {
            Runner = Name,
            ScenarioId = scenario.Id,
            Text = string.Concat(output),
            Events = [],
            Metadata = new Dictionary<string, string>
            {
                ["mode"] = scenario.InitialState.Mode,
                ["ime"] = scenario.InitialState.Ime,
                ["scope"] = "core-chord-engine"
            }
        });
    }
}

public static class LegacyKeymapLoader
{
    public static Keymap<string> Load(string mode)
    {
        var normalizedMode = mode.Trim().ToUpperInvariant();
        if (normalizedMode is not ("S" or "K"))
            throw new NotSupportedException($"Legacy chord keymap mode '{mode}' is not supported by the core runner.");

        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var singles = root.GetProperty("singleStroke")
            .GetProperty(normalizedMode)
            .EnumerateObject()
            .Select(item => new SingleMapping<string>(item.Name, item.Value.GetString() ?? string.Empty))
            .ToArray();

        var chords = root.GetProperty("chords")
            .GetProperty(normalizedMode)
            .EnumerateArray()
            .Select(item => new ChordMapping<string>(
                item[0].GetString() ?? throw new InvalidDataException("Chord first key is missing."),
                item[1].GetString() ?? throw new InvalidDataException("Chord second key is missing."),
                item[2].GetString() ?? string.Empty))
            .ToArray();

        return new Keymap<string>(singles, chords);
    }
}
