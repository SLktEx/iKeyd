using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Keymaps;

namespace iKeyd.Core.Configuration;

public sealed record AutomationProfile
{
    private readonly IReadOnlyDictionary<string, AutomationKeymapProfile> _keymaps;
    private readonly IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> _behaviorDefinitions;

    public AutomationProfile(
        int chordWindowMs,
        IEnumerable<AutomationKeymapProfile> keymaps,
        string startupMode = "S",
        IEnumerable<HotkeyBinding>? hotkeys = null,
        IEnumerable<UserBehaviorDefinitionProfile>? behaviorDefinitions = null,
        ClipboardHistoryProfile? clipboard = null,
        MouseMotionProfile? mouse = null)
    {
        if (chordWindowMs < 0)
            throw new ArgumentOutOfRangeException(nameof(chordWindowMs));
        ArgumentNullException.ThrowIfNull(keymaps);
        if (string.IsNullOrWhiteSpace(startupMode))
            throw new ArgumentException("Startup mode must not be empty.", nameof(startupMode));

        ChordWindowMs = chordWindowMs;
        StartupMode = startupMode;
        Hotkeys = (hotkeys ?? []).ToArray();
        Clipboard = clipboard ?? ClipboardHistoryProfile.Default;
        Mouse = mouse ?? MouseMotionProfile.Default;

        var byName = new Dictionary<string, AutomationKeymapProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var keymap in keymaps)
        {
            ArgumentNullException.ThrowIfNull(keymap);
            if (!byName.TryAdd(keymap.Name, keymap))
                throw new ArgumentException($"Duplicate keymap name '{keymap.Name}'.", nameof(keymaps));
        }
        _keymaps = byName;

        var definitions = new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in behaviorDefinitions ?? [])
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!definitions.TryAdd(definition.Name, definition))
                throw new ArgumentException($"Duplicate behavior definition '{definition.Name}'.", nameof(behaviorDefinitions));
            if (definition.Name.Equals("LT", StringComparison.OrdinalIgnoreCase) ||
                definition.Name.Equals("MT", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Behavior definition '{definition.Name}' conflicts with a standard behavior.", nameof(behaviorDefinitions));
            }
        }
        _behaviorDefinitions = definitions;
    }

    public int ChordWindowMs { get; }
    public string StartupMode { get; }
    public IReadOnlyList<HotkeyBinding> Hotkeys { get; }
    public ClipboardHistoryProfile Clipboard { get; }
    public MouseMotionProfile Mouse { get; }
    public IReadOnlyDictionary<string, AutomationKeymapProfile> Keymaps => _keymaps;
    public IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> BehaviorDefinitions => _behaviorDefinitions;

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
        IEnumerable<ChordMapping<string>> chordMappings,
        IEnumerable<BehaviorMappingProfile>? behaviorMappings = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Keymap name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(singleMappings);
        ArgumentNullException.ThrowIfNull(chordMappings);

        Name = name;
        SingleMappings = singleMappings.ToArray();
        ChordMappings = chordMappings.ToArray();
        BehaviorMappings = (behaviorMappings ?? []).ToArray();

        var behaviorKeys = new HashSet<KeyId>();
        foreach (var mapping in BehaviorMappings)
        {
            ArgumentNullException.ThrowIfNull(mapping);
            if (!behaviorKeys.Add(mapping.Key))
                throw new ArgumentException($"Duplicate behavior mapping '{Name}.{mapping.Key}'.", nameof(behaviorMappings));
        }

        var singleKeys = new HashSet<KeyId>(SingleMappings.Select(mapping => mapping.Key));
        var conflict = behaviorKeys.FirstOrDefault(singleKeys.Contains);
        if (behaviorKeys.Count != 0 && singleKeys.Contains(conflict))
            throw new ArgumentException($"Key '{Name}.{conflict}' cannot have both a string mapping and a behavior mapping.", nameof(behaviorMappings));
    }

    public string Name { get; }
    public IReadOnlyList<SingleMapping<string>> SingleMappings { get; }
    public IReadOnlyList<ChordMapping<string>> ChordMappings { get; }
    public IReadOnlyList<BehaviorMappingProfile> BehaviorMappings { get; }

    public Keymap<string> BuildKeymap() => new(SingleMappings, ChordMappings);

    public IReadOnlyDictionary<KeyId, BehaviorDefinition> BuildBehaviorBindings()
        => BuildBehaviorBindings(new Dictionary<string, UserBehaviorDefinitionProfile>(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<KeyId, BehaviorDefinition> BuildBehaviorBindings(
        IReadOnlyDictionary<string, UserBehaviorDefinitionProfile> userDefinitions)
    {
        ArgumentNullException.ThrowIfNull(userDefinitions);
        var result = new Dictionary<KeyId, BehaviorDefinition>();
        foreach (var mapping in BehaviorMappings)
            result.Add(mapping.Key, BehaviorDefinitionFactory.Create(mapping.Invocation, userDefinitions));
        return result;
    }
}

public sealed record HotkeyBinding(string Trigger, string Action);
