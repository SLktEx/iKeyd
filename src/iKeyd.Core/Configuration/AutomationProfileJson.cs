using System.Text.Json;
using iKeyd.Core.Chords;

namespace iKeyd.Core.Configuration;

public static class AutomationProfileJson
{
    public static AutomationProfile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Configuration path must not be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Automation profile was not found.", path);

        return Parse(File.ReadAllText(path));
    }

    public static AutomationProfile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Profile JSON must not be empty.", nameof(json));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var chordWindowMs = ChordEngine<string>.DefaultChordWindowMs;
        if (root.TryGetProperty("source", out var source) &&
            source.TryGetProperty("chordWindowMs", out var chordWindow))
        {
            chordWindowMs = chordWindow.GetInt32();
        }
        if (chordWindowMs < 0)
            throw new InvalidDataException("source.chordWindowMs must be non-negative.");

        var startupMode = "S";
        if (root.TryGetProperty("startupMode", out var startupModeElement))
            startupMode = startupModeElement.GetString() ?? throw new InvalidDataException("startupMode must be a string.");
        if (string.IsNullOrWhiteSpace(startupMode))
            throw new InvalidDataException("startupMode must not be empty.");

        if (!root.TryGetProperty("singleStroke", out var singleRoot) || singleRoot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("singleStroke must be an object.");
        if (!root.TryGetProperty("chords", out var chordRoot) || chordRoot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("chords must be an object.");

        var modeNames = singleRoot.EnumerateObject().Select(property => property.Name)
            .Concat(chordRoot.EnumerateObject().Select(property => property.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var keymaps = new List<AutomationKeymapProfile>();
        foreach (var mode in modeNames)
        {
            if (!TryGetPropertyIgnoreCase(singleRoot, mode, out var singlesElement))
                throw new InvalidDataException($"singleStroke.{mode} is missing from the profile.");
            if (!TryGetPropertyIgnoreCase(chordRoot, mode, out var chordsElement))
                throw new InvalidDataException($"chords.{mode} is missing from the profile.");
            if (singlesElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"singleStroke.{mode} must be an object.");
            if (chordsElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"chords.{mode} must be an array.");

            var singles = singlesElement.EnumerateObject()
                .Select(item => new SingleMapping<string>(item.Name, item.Value.GetString() ?? string.Empty))
                .ToArray();

            var chords = new List<ChordMapping<string>>();
            foreach (var item in chordsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 3)
                    throw new InvalidDataException($"A chords.{mode} entry must contain [first, second, output].");

                chords.Add(new ChordMapping<string>(
                    item[0].GetString() ?? throw new InvalidDataException("Chord first key is missing."),
                    item[1].GetString() ?? throw new InvalidDataException("Chord second key is missing."),
                    item[2].GetString() ?? string.Empty));
            }

            keymaps.Add(new AutomationKeymapProfile(mode, singles, chords));
        }

        var hotkeys = new List<HotkeyBinding>();
        if (root.TryGetProperty("hotkeys", out var hotkeysElement))
        {
            if (hotkeysElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("hotkeys must be an array.");

            foreach (var item in hotkeysElement.EnumerateArray())
            {
                var trigger = item.GetProperty("trigger").GetString()
                    ?? throw new InvalidDataException("A hotkey trigger must be a string.");
                var action = item.GetProperty("action").GetString()
                    ?? throw new InvalidDataException("A hotkey action must be a string.");
                hotkeys.Add(new HotkeyBinding(trigger, action));
            }
        }

        var keyBehaviors = KeyBehaviorProfileJson.Parse(root);
        return new AutomationProfile(chordWindowMs, keymaps, startupMode, hotkeys, keyBehaviors);
    }

    public static void Save(AutomationProfile profile, string path)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Output path must not be empty.", nameof(path));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        Write(profile, writer);
    }

    public static string Serialize(AutomationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            Write(profile, writer);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(AutomationProfile profile, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteNumber("chordWindowMs", profile.ChordWindowMs);
        writer.WriteEndObject();
        writer.WriteString("startupMode", profile.StartupMode);

        writer.WritePropertyName("singleStroke");
        writer.WriteStartObject();
        foreach (var keymap in profile.Keymaps.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(keymap.Name);
            writer.WriteStartObject();

            // AHK single-stroke variables are last-write-wins. Emit the effective
            // mapping while keeping chord declaration order separately below.
            var effectiveSingles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in keymap.SingleMappings)
                effectiveSingles[mapping.Key.Value] = mapping.Output;
            foreach (var mapping in effectiveSingles.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                writer.WriteString(mapping.Key, mapping.Value);

            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WritePropertyName("chords");
        writer.WriteStartObject();
        foreach (var keymap in profile.Keymaps.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(keymap.Name);
            writer.WriteStartArray();
            foreach (var mapping in keymap.ChordMappings)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(mapping.First.Value);
                writer.WriteStringValue(mapping.Second.Value);
                writer.WriteStringValue(mapping.Output);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();

        if (profile.Hotkeys.Count > 0)
        {
            writer.WritePropertyName("hotkeys");
            writer.WriteStartArray();
            foreach (var hotkey in profile.Hotkeys)
            {
                writer.WriteStartObject();
                writer.WriteString("trigger", hotkey.Trigger);
                writer.WriteString("action", hotkey.Action);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        KeyBehaviorProfileJson.Write(profile.KeyBehaviors, writer);

        writer.WriteEndObject();
        writer.Flush();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
