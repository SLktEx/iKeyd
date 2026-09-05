using iKeyd.Core.Configuration;
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

    public static IKeydConfiguration Load(string path)
    {
        var profile = AutomationProfileJson.Load(path);
        if (!Enum.TryParse<InputMode>(profile.StartupMode, ignoreCase: true, out var startupMode))
            throw new InvalidDataException($"Unsupported startupMode '{profile.StartupMode}' for the Windows app.");

        // Windows v1 still exposes the hotkeySKG S/K modes. Validate those
        // profile requirements here instead of teaching the generic runtime about them.
        _ = profile.GetKeymap("S");
        _ = profile.GetKeymap("K");
        return new IKeydConfiguration(profile, startupMode);
    }

    public static string GetKeymapName(KeymapMode mode)
        => mode switch
        {
            KeymapMode.S => "S",
            KeymapMode.K => "K",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}
