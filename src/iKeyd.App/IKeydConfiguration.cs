using System.Text.Json;
using iKeyd.Core.Chords;
using iKeyd.Core.Keymaps;
using iKeyd.Core.Modes;

namespace iKeyd.App;

public sealed record IKeydConfiguration(
    int ChordWindowMs,
    Keymap<string> SKeymap,
    Keymap<string> KKeymap,
    InputMode StartupMode)
{
    public static IKeydConfiguration Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Configuration path must not be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("iKeyd configuration file was not found.", path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var window = root.TryGetProperty("source", out var source) &&
                     source.TryGetProperty("chordWindowMs", out var chordWindow)
            ? chordWindow.GetInt32()
            : ChordEngine<string>.DefaultChordWindowMs;
        if (window < 0)
            throw new InvalidDataException("source.chordWindowMs must be non-negative.");

        var startupMode = InputMode.S;
        if (root.TryGetProperty("startupMode", out var startupModeElement))
        {
            var mode = startupModeElement.GetString();
            if (!Enum.TryParse<InputMode>(mode, ignoreCase: true, out startupMode))
                throw new InvalidDataException($"Unsupported startupMode '{mode}'.");
        }

        return new IKeydConfiguration(
            window,
            LoadKeymap(root, "S"),
            LoadKeymap(root, "K"),
            startupMode);
    }

    public Keymap<string> GetKeymap(KeymapMode mode)
        => mode switch
        {
            KeymapMode.S => SKeymap,
            KeymapMode.K => KKeymap,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static Keymap<string> LoadKeymap(JsonElement root, string mode)
    {
        if (!root.TryGetProperty("singleStroke", out var singleStroke) ||
            !singleStroke.TryGetProperty(mode, out var singlesElement))
        {
            throw new InvalidDataException($"singleStroke.{mode} is missing from configuration.");
        }

        if (!root.TryGetProperty("chords", out var chords) ||
            !chords.TryGetProperty(mode, out var chordsElement))
        {
            throw new InvalidDataException($"chords.{mode} is missing from configuration.");
        }

        var singles = singlesElement.EnumerateObject()
            .Select(item => new SingleMapping<string>(item.Name, item.Value.GetString() ?? string.Empty))
            .ToArray();

        var chordMappings = new List<ChordMapping<string>>();
        foreach (var item in chordsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 3)
                throw new InvalidDataException($"A chords.{mode} entry must contain [first, second, output].");

            chordMappings.Add(new ChordMapping<string>(
                item[0].GetString() ?? throw new InvalidDataException("Chord first key is missing."),
                item[1].GetString() ?? throw new InvalidDataException("Chord second key is missing."),
                item[2].GetString() ?? string.Empty));
        }

        return new Keymap<string>(singles, chordMappings);
    }
}
