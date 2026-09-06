using iKeyd.Core.Configuration;
using iKeyd.Core.Keymaps;
using iKeyd.Profiles.HotkeySkg.Modes;

namespace iKeyd.App;

/// <summary>
/// Windows-app projection of the platform-neutral automation profile.
/// Core owns profile loading and named keymaps; this type only applies the
/// hotkeySKG-compatible S/K/T/R startup-mode policy used by the Windows UI.
/// </summary>
public sealed record IKeydConfiguration
{
    public IKeydConfiguration(AutomationProfile profile, InputMode startupMode)
        : this(
            profile,
            startupMode,
            profile.GetKeymap("S").BuildKeymap(),
            profile.GetKeymap("K").BuildKeymap())
    {
    }

    internal IKeydConfiguration(
        AutomationProfile profile,
        InputMode startupMode,
        Keymap<string> sKeymap,
        Keymap<string> kKeymap)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        StartupMode = startupMode;
        SKeymap = sKeymap ?? throw new ArgumentNullException(nameof(sKeymap));
        KKeymap = kKeymap ?? throw new ArgumentNullException(nameof(kKeymap));
    }

    public AutomationProfile Profile { get; init; }
    public InputMode StartupMode { get; init; }
    public int ChordWindowMs => Profile.ChordWindowMs;
    public MouseMotionProfile Mouse => Profile.Mouse;
    public Keymap<string> SKeymap { get; }
    public Keymap<string> KKeymap { get; }

    public static IKeydConfiguration Load(string path)
    {
        var profile = AutomationProfileJson.Load(path);
        var json = File.ReadAllText(path);
        return FromProfile(MouseMotionProfileJson.Apply(profile, json));
    }

    public static IKeydConfiguration Parse(string json)
    {
        var profile = AutomationProfileJson.Parse(json);
        return FromProfile(MouseMotionProfileJson.Apply(profile, json));
    }

    private static IKeydConfiguration FromProfile(AutomationProfile profile)
    {
        if (!Enum.TryParse<InputMode>(profile.StartupMode, ignoreCase: true, out var startupMode))
            throw new InvalidDataException($"Unsupported startupMode '{profile.StartupMode}' for the Windows app.");

        // Windows v1 still exposes the hotkeySKG S/K modes. Validate those
        // profile requirements here instead of teaching generic Core APIs about them.
        _ = profile.GetKeymap("S");
        _ = profile.GetKeymap("K");
        return new IKeydConfiguration(profile, startupMode);
    }

    public Keymap<string> GetKeymap(KeymapMode mode)
        => mode switch
        {
            KeymapMode.S => SKeymap,
            KeymapMode.K => KKeymap,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}
