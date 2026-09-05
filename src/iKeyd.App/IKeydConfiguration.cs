using iKeyd.Core.Configuration;
using iKeyd.Core.Keymaps;
using iKeyd.Profiles.HotkeySkg.Modes;

namespace iKeyd.App;

/// <summary>
/// Windows-app projection of the platform-neutral automation profile.
/// Core owns profile loading and named keymaps; this type only applies the
/// hotkeySKG-compatible S/K/T/R startup-mode policy used by the Windows UI.
/// </summary>
public sealed record IKeydConfiguration(
    AutomationProfile Profile,
    InputMode StartupMode)
{
    public int ChordWindowMs => Profile.ChordWindowMs;
    public Keymap<string> SKeymap => Profile.GetKeymap("S").BuildKeymap();
    public Keymap<string> KKeymap => Profile.GetKeymap("K").BuildKeymap();

    public static IKeydConfiguration Load(string path)
    {
        var profile = AutomationProfileJson.Load(path);
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
