using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Feeds full hotkeySKG layer input (Muhenkan/Henkan/Space/Kana and ordinary
/// keys) to an existing hosted legacy runner without expanding the original
/// text/chord runner's bootstrap contract. A harmless key-up event keeps the
/// inner process alive while this wrapper injects the real scenario after the
/// hosted T-mode switch has completed.
/// </summary>
public sealed class RuntimeInputInjectionRunner : ICompatibilityScenarioRunner
{
    private const nuint ForeignMarker = (nuint)0x24681357U;
    private const uint KeyEventKeyUp = 0x0002;
    private readonly ICompatibilityScenarioRunner _inner;

    public RuntimeInputInjectionRunner(ICompatibilityScenarioRunner inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public string Name => _inner.Name + " + runtime-input";
    public bool IsAvailable => _inner.IsAvailable;

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var configuredExecutable = Environment.GetEnvironmentVariable(
            LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable);
        var processName = HostedTModeLegacyRunner.ResolveLegacyProcessName(configuredExecutable);

        // HostedAutoHotkeySourceRunner sets IKEYD_LEGACY_EXE only after entering
        // its temporary environment gate, but its copied interpreter is always
        // named hotkeySKG.exe.
        if (_inner is HostedAutoHotkeySourceRunner)
            processName = "hotkeySKG";

        var adapted = scenario with
        {
            Input =
            [
                // A lone key-up is ignored by the legacy state machine. The
                // HostedTMode adapter adds another 500 ms, so this keeps the
                // process/capture alive long enough for the real injection below.
                new ScenarioInputEvent { AtMs = 800, Kind = "keyUp", Key = "Q" }
            ]
        };

        using var injectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var injectionTask = InjectWhenReadyAsync(processName, scenario.Input, injectionCancellation.Token);

        try
        {
            var result = await _inner.RunAsync(adapted, cancellationToken);
            await injectionTask;
            return result with
            {
                Runner = Name,
                Metadata = new Dictionary<string, string>(result.Metadata)
                {
                    ["runtimeInput"] = "externally-injected-after-hosted-mode-switch"
                }
            };
        }
        finally
        {
            injectionCancellation.Cancel();
            if (!injectionTask.IsCompleted)
            {
                try
                {
                    await injectionTask;
                }
                catch (OperationCanceledException) when (injectionCancellation.IsCancellationRequested)
                {
                }
            }
        }
    }

    private static async Task InjectWhenReadyAsync(
        string processName,
        IReadOnlyList<ScenarioInputEvent> inputs,
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
                    // HostedTModeLegacyRunner sends its M+digit control sequence
                    // after 650 ms. LegacyExecutableScenarioRunner starts output
                    // capture after its 750 ms startup wait. 900 ms is therefore
                    // safely after both without making each scenario slow.
                    await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);

                    var stopwatch = Stopwatch.StartNew();
                    foreach (var input in inputs)
                    {
                        var remaining = input.AtMs - stopwatch.ElapsedMilliseconds;
                        if (remaining > 0)
                            await Task.Delay(TimeSpan.FromMilliseconds(remaining), cancellationToken);
                        Send(input);
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

        throw new TimeoutException($"The legacy process '{processName}' did not become available for runtime input injection.");
    }

    private static void Send(ScenarioInputEvent input)
    {
        var virtualKey = ScenarioKeyboard.ResolveVirtualKey(input.Key!);
        var flags = string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase)
            ? KeyEventKeyUp
            : 0u;
        NativeMethods.keybd_event((byte)virtualKey, 0, flags, ForeignMarker);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
