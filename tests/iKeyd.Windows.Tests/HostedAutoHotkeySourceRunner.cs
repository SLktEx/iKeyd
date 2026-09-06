using System.Security.Cryptography;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Runs the original hotkeySKG.ahk source as a distinct compatibility oracle.
/// The AutoHotkey interpreter is copied to hotkeySKG.exe beside hotkeySKG.ahk,
/// allowing AutoHotkey v1 to auto-load the same-basename script while reusing
/// a supplied legacy-process scenario runner.
/// </summary>
public sealed class HostedAutoHotkeySourceRunner : ICompatibilityScenarioRunner
{
    public const string InterpreterEnvironmentVariable = "IKEYD_AHK_V1_EXE";
    public const string SourceEnvironmentVariable = "IKEYD_LEGACY_AHK";
    public const string ReferenceSourceSha256 = "fde46d179a2cfb8123a314d4ea6b8de714a65302867d4b3a654af07f9472bab7";
    public const string RuntimeVersion = "AutoHotkey v1.1.16.05";

    private static readonly SemaphoreSlim EnvironmentGate = new(1, 1);
    private readonly Func<ICompatibilityScenarioRunner> _runnerFactory;

    public HostedAutoHotkeySourceRunner()
        : this(() => new HostedTModeLegacyRunner())
    {
    }

    internal HostedAutoHotkeySourceRunner(Func<ICompatibilityScenarioRunner> runnerFactory)
        => _runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));

    public string Name => "hotkeySKG.ahk + AutoHotkey v1.1.16.05";

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;

            var interpreter = Environment.GetEnvironmentVariable(InterpreterEnvironmentVariable);
            var source = Environment.GetEnvironmentVariable(SourceEnvironmentVariable);
            return !string.IsNullOrWhiteSpace(interpreter) && File.Exists(interpreter) &&
                   !string.IsNullOrWhiteSpace(source) && File.Exists(source);
        }
    }

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The AutoHotkey source runner requires Windows.");

        var interpreter = ResolveRequiredFile(InterpreterEnvironmentVariable, "AutoHotkey v1 interpreter");
        var source = ResolveRequiredFile(SourceEnvironmentVariable, "legacy AutoHotkey source");
        var sourceSha256 = ComputeSha256(source);
        if (!string.Equals(sourceSha256, ReferenceSourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Legacy AHK source SHA-256 mismatch. Expected {ReferenceSourceSha256}, actual {sourceSha256}.");
        }

        var interpreterSha256 = ComputeSha256(interpreter);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ikeyd-ahk-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        var hostedInterpreter = Path.Combine(temporaryDirectory, "hotkeySKG.exe");
        var hostedSource = Path.Combine(temporaryDirectory, "hotkeySKG.ahk");
        File.Copy(interpreter, hostedInterpreter, overwrite: true);
        File.Copy(source, hostedSource, overwrite: true);

        await EnvironmentGate.WaitAsync(cancellationToken);
        var previousExecutable = Environment.GetEnvironmentVariable(LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable);
        var previousExecutableSha = Environment.GetEnvironmentVariable(LegacyExecutableScenarioRunner.ExpectedSha256EnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable, hostedInterpreter);
            Environment.SetEnvironmentVariable(LegacyExecutableScenarioRunner.ExpectedSha256EnvironmentVariable, interpreterSha256);

            var result = await _runnerFactory().RunAsync(scenario, cancellationToken);
            var metadata = new Dictionary<string, string>(result.Metadata);
            metadata.Remove("sha256");
            metadata["oracle"] = "ahk-v1-source";
            metadata["runtime"] = RuntimeVersion;
            metadata["sourceSha256"] = sourceSha256;
            metadata["interpreterSha256"] = interpreterSha256;
            metadata["scope"] = result.Metadata.TryGetValue("scope", out var scope)
                ? $"legacy-ahk-source:{scope}"
                : "legacy-ahk-source";

            return result with
            {
                Runner = Name,
                Metadata = metadata
            };
        }
        finally
        {
            Environment.SetEnvironmentVariable(LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable, previousExecutable);
            Environment.SetEnvironmentVariable(LegacyExecutableScenarioRunner.ExpectedSha256EnvironmentVariable, previousExecutableSha);
            EnvironmentGate.Release();

            try
            {
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch
            {
                // Cleanup must not mask the compatibility result.
            }
        }
    }

    private static string ResolveRequiredFile(string environmentVariable, string description)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Set {environmentVariable} to the {description} path.");

        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} was not found.", path);
        return path;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
