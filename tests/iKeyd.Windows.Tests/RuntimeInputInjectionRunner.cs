using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

public sealed class RuntimeInputInjectionRunner : ICompatibilityScenarioRunner
{
    private const nuint ForeignMarker = (nuint)0x24681357U;
    private const uint KeyEventKeyUp = 0x0002;
    private readonly ICompatibilityScenarioRunner _inner;

    public RuntimeInputInjectionRunner(ICompatibilityScenarioRunner inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    public string Name => _inner.Name + " + runtime-input";
    public bool IsAvailable => _inner.IsAvailable;

    public async Task<ScenarioRunResult> RunAsync(CompatibilityScenario scenario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var configuredExecutable = Environment.GetEnvironmentVariable(LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable);
        var processName = HostedTModeLegacyRunner.ResolveLegacyProcessName(configuredExecutable);
        if (_inner is HostedAutoHotkeySourceRunner) processName = "hotkeySKG";

        var adapted = scenario with { Input = [new ScenarioInputEvent { AtMs = 800, Kind = "keyUp", Key = "Q" }] };
        using var injectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var injectionTask = InjectWhenReadyAsync(processName, scenario.Input, injectionCancellation.Token);
        try
        {
            var result = await _inner.RunAsync(adapted, cancellationToken);
            await injectionTask;
            return result with
            {
                Runner = Name,
                Metadata = new Dictionary<string, string>(result.Metadata) { ["runtimeInput"] = "externally-injected-after-hosted-mode-switch" }
            };
        }
        finally
        {
            injectionCancellation.Cancel();
            if (!injectionTask.IsCompleted)
            {
                try { await injectionTask; }
                catch (OperationCanceledException) when (injectionCancellation.IsCancellationRequested) { }
            }
        }
    }

    private static async Task InjectWhenReadyAsync(string processName, IReadOnlyList<ScenarioInputEvent> inputs, CancellationToken cancellationToken)
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
                    await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
                    var stopwatch = Stopwatch.StartNew();
                    foreach (var input in inputs)
                    {
                        var remaining = input.AtMs - stopwatch.ElapsedMilliseconds;
                        if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), cancellationToken);
                        Send(input);
                    }
                    return;
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
        throw new TimeoutException($"The legacy process '{processName}' did not become available for runtime input injection.");
    }

    private static void Send(ScenarioInputEvent input)
    {
        var virtualKey = ScenarioKeyboard.ResolveVirtualKey(input.Key!);
        var scanCode = ScenarioKeyboard.ResolveScanCode(input.Key!);
        var flags = string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase) ? KeyEventKeyUp : 0u;
        NativeMethods.keybd_event((byte)virtualKey, scanCode, flags, ForeignMarker);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
