using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Adapts the real legacy executable for GitHub-hosted Windows runners where
/// installing and activating a Japanese IME in the same ephemeral session is
/// unreliable. The legacy script's T mode bypasses IME_IfRomaKana(). Before
/// entering T mode, the adapter can select either the S or K keymap; process3()
/// changes only gmode, so the selected gimode remains active in T mode.
/// </summary>
public sealed class HostedTModeLegacyRunner : ICompatibilityScenarioRunner
{
    private const nuint ForeignMarker = (nuint)0x24681357U;
    private const byte VkNonConvert = 0x1D;
    private const byte Vk3 = 0x33;
    private const byte Vk4 = 0x34;
    private const byte NonConvertScanCode = 0x7B;
    private const uint KeyEventKeyUp = 0x0002;
    private const long ScenarioDelayMs = 1000;
    private static readonly TimeSpan HookStartupDelay = TimeSpan.FromMilliseconds(900);

    private readonly LegacyExecutableScenarioRunner _inner = new();

    public string Name => _inner.Name;
    public bool IsAvailable => _inner.IsAvailable;

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var requestedKeymap = NormalizeHostedKeymap(scenario.InitialState.Mode);
        var hostedScenario = PrepareScenario(scenario);
        var executablePath = Environment.GetEnvironmentVariable(
            LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable);
        var processName = ResolveLegacyProcessName(executablePath);

        using var switchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var switchTask = SwitchLegacyToTModeWhenReadyAsync(
            processName,
            requestedKeymap,
            switchCancellation.Token);

        try
        {
            var result = await _inner.RunAsync(hostedScenario, cancellationToken);
            await switchTask;

            var metadata = new Dictionary<string, string>(result.Metadata)
            {
                ["legacyMode"] = "T",
                ["legacyKeymap"] = requestedKeymap,
                ["scenarioMode"] = scenario.InitialState.Mode,
                ["ime"] = "bypassed-via-T-mode",
                ["hostedAdapter"] = nameof(HostedTModeLegacyRunner),
                ["legacyProcessName"] = processName
            };

            return result with { Metadata = metadata };
        }
        finally
        {
            switchCancellation.Cancel();
            if (!switchTask.IsCompleted)
            {
                try
                {
                    await switchTask;
                }
                catch (OperationCanceledException) when (switchCancellation.IsCancellationRequested)
                {
                    // Preserve the primary runner failure if cleanup cancels the mode switch task.
                }
            }
        }
    }

    internal static CompatibilityScenario PrepareScenario(CompatibilityScenario scenario)
    {
        _ = NormalizeHostedKeymap(scenario.InitialState.Mode);

        // LegacyExecutableScenarioRunner intentionally models the normal startup
        // state and therefore accepts S mode only. Hosted mode selection is done
        // explicitly with the legacy M+digit control chords below, so present an
        // S/off bootstrap state to the inner process harness while preserving the
        // original scenario for the iKeyd side of the differential comparison.
        // Keep scenario input well after the mode-selection hook startup window;
        // otherwise M+4/M+3 can be injected before AHK has installed its hooks and
        // the test observes raw characters such as "fu" instead of K chords.
        return scenario with
        {
            InitialState = scenario.InitialState with { Mode = "S", Ime = "off" },
            Input = scenario.Input
                .Select(input => input with { AtMs = input.AtMs + ScenarioDelayMs })
                .ToList()
        };
    }

    internal static string ResolveLegacyProcessName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return "hotkeySKG";

        var name = Path.GetFileNameWithoutExtension(executablePath.Trim());
        return string.IsNullOrWhiteSpace(name) ? "hotkeySKG" : name;
    }

    internal static IReadOnlyList<byte> ResolveModeSelectionDigits(string mode)
        => NormalizeHostedKeymap(mode) switch
        {
            "S" => [Vk3],
            "K" => [Vk4, Vk3],
            _ => throw new UnreachableException()
        };

    private static string NormalizeHostedKeymap(string mode)
    {
        if (string.Equals(mode, "S", StringComparison.OrdinalIgnoreCase))
            return "S";
        if (string.Equals(mode, "K", StringComparison.OrdinalIgnoreCase))
            return "K";

        throw new NotSupportedException(
            $"The hosted T-mode legacy adapter currently supports S and K keymaps, not '{mode}'.");
    }

    private static async Task SwitchLegacyToTModeWhenReadyAsync(
        string processName,
        string requestedKeymap,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Any(process => !process.HasExited))
                {
                    // The process can become visible before the AHK low-level hooks
                    // are installed. Wait past the normal 750 ms startup guard used
                    // by the inner runner before injecting M+digit mode selection.
                    await Task.Delay(HookStartupDelay, cancellationToken);

                    var digits = ResolveModeSelectionDigits(requestedKeymap);
                    // Repeat the idempotent mode-selection sequence once. The legacy
                    // process can become visible just before every hotkey is ready.
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        for (var index = 0; index < digits.Count; index++)
                        {
                            SendModeSelectionChord(digits[index]);
                            if (index + 1 < digits.Count)
                                await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
                        }
                        if (attempt == 0)
                            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
                    }

                    return;
                }
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        throw new TimeoutException(
            $"The legacy process '{processName}' did not become available for T-mode activation.");
    }

    private static void SendModeSelectionChord(byte digitVirtualKey)
    {
        // Legacy mappings:
        //   M+4 -> process4() -> KMODE + gimode="K"
        //   M+3 -> process3() -> TMODE, leaving gimode unchanged
        // Therefore S needs only M+3, while K uses M+4 followed by M+3.
        // Reuse the normal harness marker so the legacy output capture ignores
        // these injected control events if its hook is already active.
        SendKey(VkNonConvert, NonConvertScanCode, keyUp: false);
        Thread.Sleep(20);
        SendKey(digitVirtualKey, 0, keyUp: false);
        Thread.Sleep(20);
        SendKey(digitVirtualKey, 0, keyUp: true);
        Thread.Sleep(20);
        SendKey(VkNonConvert, NonConvertScanCode, keyUp: true);
    }

    private static void SendKey(byte virtualKey, byte scanCode, bool keyUp)
        => NativeMethods.keybd_event(
            virtualKey,
            scanCode,
            keyUp ? KeyEventKeyUp : 0u,
            ForeignMarker);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
