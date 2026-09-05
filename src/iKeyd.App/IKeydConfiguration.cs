using iKeyd.Core.Configuration;
using iKeyd.Core.Keymaps;
using iKeyd.Core.Modes;

namespace iKeyd.App;

/// <summary>
/// Windows-app projection of the platform-neutral automation profile.
/// The JSON/profile format and keymap declaration parsing live in iKeyd.Core;
/// this type only selects the S/K modes required by the current Windows v1 UI.
/// </summary>
public sealed record IKeydConfiguration(
    int ChordWindowMs,
    Keymap<string> SKeymap,
    Keymap<string> KKeymap,
    InputMode StartupMode)
{
    public static IKeydConfiguration Load(string path)
    {
        var profile = AutomationProfileJson.Load(path);
        if (!Enum.TryParse<InputMode>(profile.StartupMode, ignoreCase: true, out var startupMode))
            throw new InvalidDataException($"Unsupported startupMode '{profile.StartupMode}' for the Windows app.");

        return new IKeydConfiguration(
            profile.ChordWindowMs,
            profile.GetKeymap("S").BuildKeymap(),
            profile.GetKeymap("K").BuildKeymap(),
            startupMode);
    }

    public Keymap<string> GetKeymap(KeymapMode mode)
        => mode switch
        {
            KeymapMode.S => SKeymap,
            KeymapMode.K => KKeymap,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}
