using iKeyd.Core.Modes;

namespace iKeyd.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(initiallyOwned: true, "Local\\iKeyd", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "iKeyd is already running.",
                "iKeyd",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var explicitConfigPath = GetOption(args, "--config");
            var configuration = explicitConfigPath is null
                ? GeneratedProfile.Create()
                : IKeydConfiguration.Load(explicitConfigPath);

            var modeOverride = GetOption(args, "--mode");
            if (!string.IsNullOrWhiteSpace(modeOverride))
            {
                if (!Enum.TryParse<InputMode>(modeOverride, ignoreCase: true, out var mode))
                    throw new ArgumentException($"Unsupported --mode value '{modeOverride}'. Use S, K, T, or R.");
                configuration = configuration with { StartupMode = mode };
            }

            using var context = new IKeydApplicationContext(configuration);
            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "iKeyd failed to start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was not owned, for example after an early startup failure.
            }
        }
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
