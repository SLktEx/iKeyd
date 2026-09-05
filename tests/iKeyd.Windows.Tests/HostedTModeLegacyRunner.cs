using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Adapts the real legacy executable for GitHub-hosted Windows runners where
/// installing and activating a Japanese IME in the same ephemeral session is
/// unreliable. The legacy script's T mode bypasses IME_IfRomaKana() while
/// continuing to use the current S chord table (gimode remains "S").
/// </summary>
public sealed class HostedTModeLegacyRunner : ICompatibilityScenarioRunner
{
    private const nuint ForeignMarker = (nuint)0x24681357U;
    private const byte VkNonConvert = 0x1D;
    private const byte Vk3 = 0x33;
    private const byte NonConvertScanCode = 0x7B;
    private const uint KeyEventKeyUp = 0x0002;
    private const long ScenarioDelayMs = 500;

    private readonly LegacyExecutableScenarioRunner _inner = new();

    public string Name => _inner.Name;
    public bool IsAvailable => _inner.IsAvailable;

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var hostedScenario = PrepareScenario(scenario);
        using var switchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var switchTask = SwitchLegacyToTModeWhenReadyAsync(switchCancellation.Token);

        try
        {
            var result = await _inner.RunAsync(hostedScenario, cancellationToken);
            await switchTask;

            var metadata = new Dictionary<string, string>(result.Metadata)
            {
                ["legacyMode"] = "T",
                ["legacyKeymap"] = "S",
                ["ime"] = "bypassed-via-T-mode",
                ["hostedAdapter"] = nameof(HostedTModeLegacyRunner)
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
        => scenario with
        {
            InitialState = scenario.InitialState with { Ime = "off" },
            Input = scenario.Input
                .Select(input => input with { AtMs = input.AtMs + ScenarioDelayMs })
                .ToList()
        };

    private static async Task SwitchLegacyToTModeWhenReadyAsync(CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processes = Process.GetProcessesByName("hotkeySKG");
            try
            {
                if (processes.Any(process => !process.HasExited))
                {
                    // Give the compiled AHK runtime enough time to install its hooks.
                    await Task.Delay(TimeSpan.FromMilliseconds(650), cancellationToken);
                    SendTModeChord();
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

        throw new TimeoutException("The legacy hotkeySKG process did not become available for T-mode activation.");
    }

    private static void SendTModeChord()
    {
        // Legacy mapping:
        //   vk1Dsc07B down -> fstate += "M"
        //   3 down          -> func_3() -> processes() -> process3() -> TMODE
        // Reuse the normal harness marker so the legacy output capture ignores
        // these injected control events if its hook is already active.
        SendKey(VkNonConvert, NonConvertScanCode, keyUp: false);
        Thread.Sleep(20);
        SendKey(Vk3, 0, keyUp: false);
        Thread.Sleep(20);
        SendKey(Vk3, 0, keyUp: true);
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
