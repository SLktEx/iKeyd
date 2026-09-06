using iKeyd.Core.Modes;

namespace iKeyd.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            if (IsNameEasterEggRequested(args))
            {
                ShowNameEasterEgg();
                return;
            }

            var started = RunSingleInstance(
                SingleInstanceGuard.TryAcquire,
                () => RunPrimaryInstance(args),
                () => MessageBox.Show(
                    "iKeyd is already running.",
                    "iKeyd",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information));

            if (!started)
                return;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "iKeyd failed to start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    internal static bool IsNameEasterEggRequested(IReadOnlyList<string> args) =>
        args.Any(argument => string.Equals(argument, "--why-the-name", StringComparison.OrdinalIgnoreCase));

    internal static bool RunSingleInstance(
        Func<IDisposable?> acquireInstance,
        Action runPrimaryInstance,
        Action runSecondaryInvocation)
    {
        using var instance = acquireInstance();
        if (instance is null)
        {
            runSecondaryInvocation();
            return false;
        }

        runPrimaryInstance();
        return true;
    }

    private static void ShowNameEasterEgg()
    {
        const string encodedLegacyName = "SWNoaWthd2EgS2V5IERhZW1vbg==";
        var legacyName = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedLegacyName));

        MessageBox.Show(
            $"I Key'd - I keyed it my way.\n\n[CLASSIFIED]\nOriginal internal codename:\n{legacyName}\n\nYou weren't supposed to find this.",
            "About iKeyd",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void RunPrimaryInstance(string[] args)
    {
        var explicitConfigPath = GetOption(args, "--config");
        var configuration = explicitConfigPath is null
            ? GeneratedProfile.Create()
            : IKeydConfiguration.Load(explicitConfigPath);
        var mouseProfile = explicitConfigPath is null
            ? GeneratedMouseProfile.Create()
            : configuration.Mouse;

        var modeOverride = GetOption(args, "--mode");
        if (!string.IsNullOrWhiteSpace(modeOverride))
        {
            if (!Enum.TryParse<InputMode>(modeOverride, ignoreCase: true, out var mode))
                throw new ArgumentException($"Unsupported --mode value '{modeOverride}'. Use S, K, T, or R.");
            configuration = configuration with { StartupMode = mode };
        }

        KeyboardMouseMotion.ConfigureDefaultProfile(mouseProfile);
        using var context = new IKeydApplicationContext(configuration);
        Application.Run(context);
    }

    private static string? GetOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                continue;
            if (index + 1 >= args.Count)
                throw new ArgumentException($"{option} requires a value.");
            return args[index + 1];
        }

        return null;
    }
}
