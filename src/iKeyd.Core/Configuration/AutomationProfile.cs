using iKeyd.Core.Chords;
using iKeyd.Core.Keymaps;

namespace iKeyd.Core.Configuration;

public sealed record AutomationProfile
{
    private readonly IReadOnlyDictionary<string, AutomationKeymapProfile> _keymaps;

    public AutomationProfile(
        int chordWindowMs,
        IEnumerable<AutomationKeymapProfile> keymaps,
        string startupMode = "S",
        IEnumerable<HotkeyBinding>? hotkeys = null,
        KeyBehaviorProfile? keyBehaviors = null)
    {
        if (chordWindowMs < 0)
            throw new ArgumentOutOfRangeException(nameof(chordWindowMs));
        ArgumentNullException.ThrowIfNull(keymaps);
        if (string.IsNullOrWhiteSpace(startupMode))
            throw new ArgumentException("Startup mode must not be empty.", nameof(startupMode));

        ChordWindowMs = chordWindowMs;
        StartupMode = startupMode;
        Hotkeys = (hotkeys ?? []).ToArray();
        KeyBehaviors = keyBehaviors ?? KeyBehaviorProfile.Empty;

        var byName = new Dictionary<string, AutomationKeymapProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var keymap in keymaps)
        {
            ArgumentNullException.ThrowIfNull(keymap);
            if (!byName.TryAdd(keymap.Name, keymap))
                throw new ArgumentException($"Duplicate keymap name '{keymap.Name}'.", nameof(keymaps));
        }

        _keymaps = byName;
    }

    public int ChordWindowMs { get; }
    public string StartupMode { get; }
    public IReadOnlyList<HotkeyBinding> Hotkeys { get; }
    public KeyBehaviorProfile KeyBehaviors { get; }
    public IReadOnlyDictionary<string, AutomationKeymapProfile> Keymaps => _keymaps;

    public AutomationKeymapProfile GetKeymap(string name)
    {
        if (!_keymaps.TryGetValue(name, out var keymap))
            throw new KeyNotFoundException($"Automation profile does not define keymap '{name}'.");
        return keymap;
    }
}

public sealed record AutomationKeymapProfile
{
    public AutomationKeymapProfile(
        string name,
        IEnumerable<SingleMapping<string>> singleMappings,
        IEnumerable<ChordMapping<string>> chordMappings)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Keymap name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(singleMappings);
        ArgumentNullException.ThrowIfNull(chordMappings);

        Name = name;
        SingleMappings = singleMappings.ToArray();
        ChordMappings = chordMappings.ToArray();
    }

    public string Name { get; }
    public IReadOnlyList<SingleMapping<string>> SingleMappings { get; }
    public IReadOnlyList<ChordMapping<string>> ChordMappings { get; }

    public Keymap<string> BuildKeymap() => new(SingleMappings, ChordMappings);
}

public sealed record HotkeyBinding(string Trigger, string Action);
