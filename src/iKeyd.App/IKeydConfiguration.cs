using iKeyd.Core.Configuration;
using iKeyd.Core.Keymaps;
using iKeyd.Profiles.HotkeySkg.Modes;

namespace iKeyd.App;

/// <summary>
/// Windows-app projection of the platform-neutral automation profile.
/// Core owns profile loading and named keymaps; this type applies the
/// hotkeySKG-compatible S/K/T/R startup policy plus Windows pointer settings.
/// </summary>
public sealed record IKeydConfiguration
{
    public IKeydConfiguration(
        AutomationProfile profile,
        InputMode startupMode,
        MouseMotionProfile? mouse = null)
        : this(
            profile,
            startupMode,
            profile.GetKeymap("S").BuildKeymap(),
            profile.GetKeymap("K").BuildKeymap(),
            mouse)
    {
    }

    internal IKeydConfiguration(
        AutomationProfile profile,
        InputMode startupMode,
        Keymap<string> sKeymap,
        Keymap<string> kKeymap,
        MouseMotionProfile? mouse = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        StartupMode = startupMode;
        SKeymap = sKeymap ?? throw new ArgumentNullException(nameof(sKeymap));
        KKeymap = kKeymap ?? throw new ArgumentNullException(nameof(kKeymap));
        Mouse = mouse ?? MouseMotionProfile.Default;
    }

    public AutomationProfile Profile { get; init; }
    public InputMode StartupMode { get; init; }
    public MouseMotionProfile Mouse { get; init; }
    public int ChordWindowMs => Profile.ChordWindowMs;
    public Keymap<string> SKeymap { get; }
    public Keymap<string> KKeymap { get; }

    public static IKeydConfiguration Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Automation profile was not found.", path);
        return Parse(File.ReadAllText(path));
    }

    public static IKeydConfiguration Parse(string json)
    {
        var profile = AutomationProfileJson.Parse(json);
        var mouse = MouseMotionProfileJson.Parse(json);
        return FromProfile(profile, mouse);
    }

    private static IKeydConfiguration FromProfile(
        AutomationProfile profile,
        MouseMotionProfile? mouse = null)
    {
        if (!Enum.TryParse<InputMode>(profile.StartupMode, ignoreCase: true, out var startupMode))
            throw new InvalidDataException($"Unsupported startupMode '{profile.StartupMode}' for the Windows app.");

        // Windows v1 still exposes the hotkeySKG S/K modes. Validate those
        // profile requirements here instead of teaching generic Core APIs about them.
        _ = profile.GetKeymap("S");
        _ = profile.GetKeymap("K");
        return new IKeydConfiguration(profile, startupMode, mouse);
    }

    public Keymap<string> GetKeymap(KeymapMode mode)
        => mode switch
        {
            KeymapMode.S => SKeymap,
            KeymapMode.K => KKeymap,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}
