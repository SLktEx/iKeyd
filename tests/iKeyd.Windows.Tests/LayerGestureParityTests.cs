using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using iKeyd.Compatibility.Tests;
using iKeyd.Core.Layers;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LayerGestureParityTests
{
    private static readonly char[] HeldLayers = ['M', 'H', 'S'];

    public static TheoryData<string, string, string[]> UserVisibleTwoLayerCases => new()
    {
        // Press order, release order, expected emitted key events.
        // These snapshots are independently checked against both pinned legacy oracles below.
        { "SM", "MS", ["keyDown:VK_A0", "keyDown:Enter", "keyUp:Enter", "keyUp:VK_A0"] },
        { "SM", "SM", [] },
        { "MS", "SM", ["keyDown:Enter", "keyUp:Enter"] },
        { "MS", "MS", ["keyDown:Space", "keyUp:Space"] },
        { "MH", "HM", ["keyDown:Tab", "keyUp:Tab"] },
        { "MH", "MH", ["keyDown:Control", "keyUp:Control"] },
        { "HM", "MH", ["keyDown:VK_A0", "keyDown:Tab", "keyUp:Tab", "keyUp:VK_A0"] },
        { "HM", "HM", [] },
        { "HS", "SH", ["keyDown:VK_A2", "keyDown:Space", "keyUp:Space", "keyUp:VK_A2"] },
        { "HS", "HS", ["keyDown:Space", "keyUp:Space"] },
        { "SH", "HS", ["keyDown:Space", "keyUp:Space"] },
        { "SH", "SH", ["keyDown:Control", "keyUp:Control"] },
    };

    [Theory]
    [MemberData(nameof(UserVisibleTwoLayerCases))]
    public async Task Two_layer_press_and_release_order_has_pinned_runtime_output(
        string pressOrder,
        string releaseOrder,
        string[] expectedEvents)
    {
        var scenario = BuildHeldLayerGesture($"runtime-{pressOrder}-{releaseOrder}", pressOrder, releaseOrder);
        var actual = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);

        Assert.Null(actual.Text);
        Assert.Empty(actual.Actions);
        Assert.Equal(expectedEvents, DescribeEvents(actual.Events));
    }

    [Fact]
    public void Every_MHS_press_and_release_permutation_returns_to_empty_state()
    {
        foreach (var pressOrder in Permutations("MHS"))
        {
            foreach (var releaseOrder in Permutations("MHS"))
            {
                var state = LayerRuntimeState.Empty;
                foreach (var layer in pressOrder)
                    state = LayerStateMachine.Apply(state, DownEvent(layer)).State;
                foreach (var layer in releaseOrder)
                    state = LayerStateMachine.Apply(state, UpEvent(layer)).State;

                Assert.True(
                    state.Layers.Count == 0,
                    $"Press {pressOrder}, release {releaseOrder} left layer state '{state.Layers}'.");
            }
        }
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task All_two_layer_press_release_orders_match_both_pinned_legacy_oracles()
    {
        var scenarios = new List<CompatibilityScenario>();
        foreach (var first in HeldLayers)
        {
            foreach (var second in HeldLayers)
            {
                if (second == first)
                    continue;

                var press = $"{first}{second}";
                scenarios.Add(BuildHeldLayerGesture($"layer-two-{press}-fifo", press, press));
                scenarios.Add(BuildHeldLayerGesture($"layer-two-{press}-lifo", press, new string([second, first])));
            }
        }

        await AssertMatchesBothOracles(scenarios);
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task All_MHS_press_and_release_permutations_match_both_pinned_legacy_oracles()
    {
        var scenarios = new List<CompatibilityScenario>();
        foreach (var press in Permutations("MHS"))
        {
            foreach (var release in Permutations("MHS"))
                scenarios.Add(BuildHeldLayerGesture($"layer-three-{press}-{release}", press, release));
        }

        await AssertMatchesBothOracles(scenarios);
    }

    [Fact]
    [Trait("Category", "HostedLayerGestureDifferentialE2E")]
    public async Task Special_sticky_and_immediate_action_branches_match_both_pinned_legacy_oracles()
    {
        var scenarios = new[]
        {
            // K + H, then H-up: Henkan branch.
            Scenario("layer-kh-hup",
                Down("KANA", 10), Up("KANA", 20),
                Down("CONVERT", 30), Up("CONVERT", 40)),

            // K + M + S, release Space first: Ctrl+Enter branch.
            Scenario("layer-kms-sup",
                Down("KANA", 10), Up("KANA", 20),
                Down("NONCONVERT", 30), Down("SPACE", 40),
                Up("SPACE", 50), Up("NONCONVERT", 60)),

            // A + M + S, release Space first: Alt+Enter branch.
            Scenario("layer-ams-sup",
                Down("ALT", 10), Down("KANA", 20), Up("KANA", 30), Up("ALT", 40),
                Down("NONCONVERT", 50), Down("SPACE", 60),
                Up("SPACE", 70), Up("NONCONVERT", 80)),

            // M + S + H: H-down emits Shift+Space immediately.
            Scenario("layer-msh-hdown",
                Down("NONCONVERT", 10), Down("SPACE", 20), Down("CONVERT", 30),
                Up("CONVERT", 40), Up("SPACE", 50), Up("NONCONVERT", 60)),

            // M + H + S: Space-down emits End+Enter immediately.
            Scenario("layer-mhs-sdown",
                Down("NONCONVERT", 10), Down("CONVERT", 20), Down("SPACE", 30),
                Up("SPACE", 40), Up("CONVERT", 50), Up("NONCONVERT", 60)),

            // H + M + S: Space-down emits Up+End+Enter immediately.
            Scenario("layer-hms-sdown",
                Down("CONVERT", 10), Down("NONCONVERT", 20), Down("SPACE", 30),
                Up("SPACE", 40), Up("NONCONVERT", 50), Up("CONVERT", 60)),
        };

        await AssertMatchesBothOracles(scenarios);
    }

    private static async Task AssertMatchesBothOracles(IReadOnlyList<CompatibilityScenario> scenarios)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiled = new LegacyLayerGestureScenarioRunner();
        var source = new HostedAutoHotkeySourceRunner(() => new LegacyLayerGestureScenarioRunner());
        if (!compiled.IsAvailable || !source.IsAvailable)
            return;

        var ikeydRunner = new IKeydRuntimeScenarioRunner();
        var failures = new List<string>();

        foreach (var scenario in scenarios)
        {
            try
            {
                var ikeyd = await ikeydRunner.RunAsync(scenario);
                var exe = await compiled.RunAsync(scenario);
                var ahk = await source.RunAsync(scenario);

                Compare(scenario.Id, "compiled EXE", ikeyd, exe, failures);
                Compare(scenario.Id, "AHK source", ikeyd, ahk, failures);
                Compare(scenario.Id, "legacy EXE vs AHK source", exe, ahk, failures);
            }
            catch (Exception error)
            {
                failures.Add($"{scenario.Id}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            failures.Count == 0
                ? string.Empty
                : $"{failures.Count} layer-gesture mismatches:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, failures));
    }

    private static void Compare(
        string scenarioId,
        string oracle,
        ScenarioRunResult expected,
        ScenarioRunResult actual,
        ICollection<string> failures)
    {
        if (!string.Equals(expected.Text, actual.Text, StringComparison.Ordinal))
            failures.Add($"{scenarioId} [{oracle}] text expected '{expected.Text}', actual '{actual.Text}'.");

        var expectedEvents = DescribeEvents(expected.Events);
        var actualEvents = DescribeEvents(actual.Events);
        if (!expectedEvents.SequenceEqual(actualEvents, StringComparer.Ordinal))
        {
            failures.Add(
                $"{scenarioId} [{oracle}] events expected [{string.Join(", ", expectedEvents)}], " +
                $"actual [{string.Join(", ", actualEvents)}].");
        }

        var expectedActions = expected.Actions.Select(action => $"{action.Kind}:{action.Value}").ToArray();
        var actualActions = actual.Actions.Select(action => $"{action.Kind}:{action.Value}").ToArray();
        if (!expectedActions.SequenceEqual(actualActions, StringComparer.Ordinal))
        {
            failures.Add(
                $"{scenarioId} [{oracle}] actions expected [{string.Join(", ", expectedActions)}], " +
                $"actual [{string.Join(", ", actualActions)}].");
        }
    }

    private static string[] DescribeEvents(IReadOnlyList<ObservedKeyEvent> events)
        => events.Select(item => $"{item.Kind}:{item.Key}").ToArray();

    private static CompatibilityScenario BuildHeldLayerGesture(string id, string pressOrder, string releaseOrder)
    {
        var input = new List<ScenarioInputEvent>();
        long at = 10;
        foreach (var layer in pressOrder)
        {
            input.Add(Down(KeyName(layer), at));
            at += 10;
        }
        foreach (var layer in releaseOrder)
        {
            input.Add(Up(KeyName(layer), at));
            at += 10;
        }

        return Scenario(id, input.ToArray());
    }

    private static CompatibilityScenario Scenario(string id, params ScenarioInputEvent[] input)
        => new()
        {
            Id = id,
            InitialState = new ScenarioInitialState
            {
                Mode = "S",
                Ime = "off",
                Layers = [],
                Modifiers = []
            },
            Input = input.ToList(),
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "layer-gesture"],
            RequiredEnvironment = ["hosted-windows"],
            OracleTargets = ["compiled-exe", "ahk-source", "ikeyd-runtime"]
        };

    private static ScenarioInputEvent Down(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };

    private static string KeyName(char layer)
        => layer switch
        {
            'M' => "NONCONVERT",
            'H' => "CONVERT",
            'S' => "SPACE",
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };

    private static LayerEvent DownEvent(char layer)
        => layer switch
        {
            'M' => LayerEvent.MDown,
            'H' => LayerEvent.HDown,
            'S' => LayerEvent.SpaceDown,
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };

    private static LayerEvent UpEvent(char layer)
        => layer switch
        {
            'M' => LayerEvent.MUp,
            'H' => LayerEvent.HUp,
            'S' => LayerEvent.SpaceUp,
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };

    private static IEnumerable<string> Permutations(string value)
    {
        if (value.Length <= 1)
        {
            yield return value;
            yield break;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var head = value[index];
            var tail = value.Remove(index, 1);
            foreach (var permutation in Permutations(tail))
                yield return head + permutation;
        }
    }

    /// <summary>
    /// Minimal legacy-process runner for physical layer gestures. Input events carry
    /// a private marker and the low-level capture records only key events injected by
    /// hotkeySKG, so the physical test input itself never appears in the observation.
    /// </summary>
    private sealed class LegacyLayerGestureScenarioRunner : ICompatibilityScenarioRunner
    {
        private const nuint ForeignMarker = (nuint)0x4C415952U; // "LAYR"
        private const byte VkAlt = 0x12;
        private const byte VkKana = 0x15;
        private const byte VkConvert = 0x1C;
        private const byte VkNonConvert = 0x1D;
        private const byte VkSpace = 0x20;
        private const byte Vk3 = 0x33;
        private const byte ScanKana = 0x70;
        private const byte ScanConvert = 0x79;
        private const byte ScanNonConvert = 0x7B;
        private const byte ScanSpace = 0x39;
        private const uint KeyEventKeyUp = 0x0002;

        public string Name => "hotkeySKG layer-gesture oracle";

        public bool IsAvailable
        {
            get
            {
                if (!OperatingSystem.IsWindows())
                    return false;
                var path = Environment.GetEnvironmentVariable(LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
        }

        public async Task<ScenarioRunResult> RunAsync(
            CompatibilityScenario scenario,
            CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException();

            var executable = ResolveExecutable();
            VerifySha256(executable);

            using var process = Start(executable);
            try
            {
                await Task.Delay(750, cancellationToken);
                if (process.HasExited)
                    throw new InvalidOperationException($"Legacy process exited during startup with code {process.ExitCode}.");

                // Enter T mode while preserving S keymap, matching the existing hosted oracle.
                Send(VkNonConvert, ScanNonConvert, keyUp: false);
                await Task.Delay(20, cancellationToken);
                Send(Vk3, 0, keyUp: false);
                Send(Vk3, 0, keyUp: true);
                await Task.Delay(20, cancellationToken);
                Send(VkNonConvert, ScanNonConvert, keyUp: true);
                await Task.Delay(120, cancellationToken);

                using var capture = new InjectedCapture(ForeignMarker);
                capture.Start();
                await Task.Delay(40, cancellationToken);
                capture.Clear();

                var stopwatch = Stopwatch.StartNew();
                foreach (var input in scenario.Input)
                {
                    var remaining = input.AtMs - stopwatch.ElapsedMilliseconds;
                    if (remaining > 0)
                        await Task.Delay((int)remaining, cancellationToken);
                    SendInput(input);
                }

                await Task.Delay(120, cancellationToken);
                return new ScenarioRunResult
                {
                    Runner = Name,
                    ScenarioId = scenario.Id,
                    Text = null,
                    Events = capture.Events.ToList(),
                    Actions = [],
                    Metadata = new Dictionary<string, string> { ["scope"] = "legacy-layer-gesture" }
                };
            }
            finally
            {
                Stop(process);
            }
        }

        private static void SendInput(ScenarioInputEvent input)
        {
            var (vk, scan) = Resolve(input.Key ?? string.Empty);
            Send(vk, scan, string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));
        }

        private static (byte VirtualKey, byte ScanCode) Resolve(string key)
            => key.Trim().ToUpperInvariant() switch
            {
                "ALT" => (VkAlt, 0),
                "KANA" => (VkKana, ScanKana),
                "CONVERT" or "HENKAN" => (VkConvert, ScanConvert),
                "NONCONVERT" or "MUHENKAN" => (VkNonConvert, ScanNonConvert),
                "SPACE" => (VkSpace, ScanSpace),
                _ => throw new NotSupportedException($"Unsupported layer-gesture input '{key}'.")
            };

        private static void Send(byte virtualKey, byte scanCode, bool keyUp)
            => NativeMethods.keybd_event(
                virtualKey,
                scanCode,
                keyUp ? KeyEventKeyUp : 0,
                ForeignMarker);

        private static string ResolveExecutable()
        {
            var path = Environment.GetEnvironmentVariable(LegacyExecutableScenarioRunner.ExecutableEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Legacy executable is unavailable.", path);
            return Path.GetFullPath(path);
        }

        private static void VerifySha256(string path)
        {
            var expected = Environment.GetEnvironmentVariable(LegacyExecutableScenarioRunner.ExpectedSha256EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(expected))
                expected = LegacyExecutableScenarioRunner.ReferenceSha256;
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (!string.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Legacy executable SHA-256 mismatch. Expected {expected}, actual {actual}.");
        }

        private static Process Start(string executable)
            => Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Could not start legacy executable.");

        private static void Stop(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                // Cleanup must not hide parity failures.
            }
        }

        private sealed class InjectedCapture : IDisposable
        {
            private const int WhKeyboardLl = 13;
            private const uint WmQuit = 0x0012;
            private const uint WmKeyDown = 0x0100;
            private const uint WmKeyUp = 0x0101;
            private const uint WmSysKeyDown = 0x0104;
            private const uint WmSysKeyUp = 0x0105;
            private const uint PmNoRemove = 0;
            private const uint LlkhfInjected = 0x10;

            private readonly nuint _foreignMarker;
            private readonly object _gate = new();
            private readonly HookProc _hookProc;
            private readonly ManualResetEventSlim _started = new(false);
            private readonly List<ObservedKeyEvent> _events = [];
            private Thread? _thread;
            private uint _threadId;
            private nint _hook;
            private Exception? _startError;

            public InjectedCapture(nuint foreignMarker)
            {
                _foreignMarker = foreignMarker;
                _hookProc = HookCallback;
            }

            public IReadOnlyList<ObservedKeyEvent> Events
            {
                get
                {
                    lock (_gate)
                        return _events.ToArray();
                }
            }

            public void Start()
            {
                _thread = new Thread(ThreadMain) { IsBackground = true, Name = "iKeyd.LayerGestureCapture" };
                _thread.Start();
                _started.Wait();
                if (_startError is not null)
                    throw new InvalidOperationException("Could not start layer-gesture capture.", _startError);
            }

            public void Clear()
            {
                lock (_gate)
                    _events.Clear();
            }

            public void Dispose()
            {
                if (_thread is not null)
                {
                    if (_threadId != 0)
                        NativeMethods.PostThreadMessageW(_threadId, WmQuit, 0, 0);
                    if (!ReferenceEquals(Thread.CurrentThread, _thread))
                        _thread.Join(TimeSpan.FromSeconds(5));
                }
                _started.Dispose();
            }

            private void ThreadMain()
            {
                try
                {
                    _threadId = NativeMethods.GetCurrentThreadId();
                    NativeMethods.PeekMessageW(out _, 0, 0, 0, PmNoRemove);
                    var module = NativeMethods.GetModuleHandleW(null);
                    _hook = NativeMethods.SetWindowsHookExW(WhKeyboardLl, _hookProc, module, 0);
                    if (_hook == 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    _started.Set();

                    while (true)
                    {
                        var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                        if (result == 0)
                            break;
                        if (result == -1)
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        NativeMethods.TranslateMessage(ref message);
                        NativeMethods.DispatchMessageW(ref message);
                    }
                }
                catch (Exception error)
                {
                    _startError = error;
                    _started.Set();
                }
                finally
                {
                    if (_hook != 0)
                        NativeMethods.UnhookWindowsHookEx(_hook);
                    _hook = 0;
                    _threadId = 0;
                }
            }

            private nint HookCallback(int code, nuint wParam, nint lParam)
            {
                if (code >= 0 && (wParam is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp))
                {
                    var native = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                    if ((native.Flags & LlkhfInjected) != 0 && native.ExtraInfo != _foreignMarker)
                    {
                        var kind = wParam is WmKeyUp or WmSysKeyUp ? "keyUp" : "keyDown";
                        lock (_gate)
                            _events.Add(new ObservedKeyEvent { Kind = kind, Key = KeyName(native.VirtualKey) });
                    }
                }
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            }

            private static string KeyName(uint virtualKey)
            {
                if (virtualKey is >= 0x41 and <= 0x5A)
                    return ((char)virtualKey).ToString();
                if (virtualKey is >= 0x70 and <= 0x7B)
                    return $"F{virtualKey - 0x70 + 1}";

                return virtualKey switch
                {
                    0x08 => "Backspace",
                    0x09 => "Tab",
                    0x0D => "Enter",
                    0x10 => "Shift",
                    0x11 => "Control",
                    0x12 => "Alt",
                    0x1B => "Escape",
                    0x20 => "Space",
                    0x23 => "End",
                    0x26 => "Up",
                    0xA0 => "VK_A0",
                    0xA2 => "VK_A2",
                    0xA4 => "VK_A4",
                    _ => $"VK_{virtualKey:X2}"
                };
            }
        }

        private delegate nint HookProc(int code, nuint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public nuint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public nint Window;
            public uint Message;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public NativePoint Point;
            public uint Private;
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

            [DllImport("kernel32.dll")]
            public static extern uint GetCurrentThreadId();

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern nint GetModuleHandleW(string? moduleName);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern nint SetWindowsHookExW(int hookId, HookProc hookProc, nint module, uint threadId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(nint hook);

            [DllImport("user32.dll")]
            public static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern int GetMessageW(out NativeMessage message, nint window, uint min, uint max);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool TranslateMessage(ref NativeMessage message);

            [DllImport("user32.dll")]
            public static extern nint DispatchMessageW(ref NativeMessage message);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PeekMessageW(out NativeMessage message, nint window, uint min, uint max, uint removeMessage);
        }
    }
}
