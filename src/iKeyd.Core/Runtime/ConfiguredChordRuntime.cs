using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using iKeyd.Core.Keymaps;

namespace iKeyd.Core.Runtime;

/// <summary>
/// Platform-neutral runtime for the named chord keymaps in an <see cref="AutomationProfile"/>.
/// It has no knowledge of hotkeySKG's S/K names, layer policy, IME routing, or any OS backend.
/// </summary>
public sealed class ConfiguredChordRuntime
{
    private readonly Dictionary<string, Keymap<string>> _keymaps;
    private readonly Dictionary<string, ChordEngine<string>> _engines;

    public ConfiguredChordRuntime(AutomationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        _keymaps = new Dictionary<string, Keymap<string>>(StringComparer.OrdinalIgnoreCase);
        _engines = new Dictionary<string, ChordEngine<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in profile.Keymaps)
        {
            var keymap = pair.Value.BuildKeymap();
            _keymaps.Add(pair.Key, keymap);
            _engines.Add(pair.Key, new ChordEngine<string>(keymap, profile.ChordWindowMs));
        }
    }

    public AutomationProfile Profile { get; }
    public IReadOnlyCollection<string> KeymapNames => _engines.Keys;

    public bool TryGetSingle(string keymap, KeyId key, out string output)
        => GetKeymap(keymap).TryGetSingle(key, out output);

    public ChordEngineState GetState(string keymap)
        => GetEngine(keymap).State;

    public IReadOnlyList<string> AdvanceTo(string keymap, long timestampMs)
        => GetEngine(keymap).AdvanceTo(timestampMs);

    public IReadOnlyList<string> OnKeyDown(string keymap, KeyId key, long timestampMs)
        => GetEngine(keymap).OnKeyDown(key, timestampMs);

    public IReadOnlyList<string> Flush(string keymap)
        => GetEngine(keymap).Flush();

    public void Cancel(string keymap)
        => GetEngine(keymap).Cancel();

    public IReadOnlyList<string> FlushAll()
    {
        var output = new List<string>();
        foreach (var engine in _engines.Values)
            output.AddRange(engine.Flush());
        return output;
    }

    public void CancelAll()
    {
        foreach (var engine in _engines.Values)
            engine.Cancel();
    }

    private Keymap<string> GetKeymap(string keymap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keymap);
        if (!_keymaps.TryGetValue(keymap, out var result))
            throw new KeyNotFoundException($"Keymap '{keymap}' is not configured.");
        return result;
    }

    private ChordEngine<string> GetEngine(string keymap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keymap);
        if (!_engines.TryGetValue(keymap, out var engine))
            throw new KeyNotFoundException($"Keymap '{keymap}' is not configured.");
        return engine;
    }
}
