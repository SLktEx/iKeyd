using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using iKeyd.Compatibility.Tests;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacyAltLayerPhysicalDifferentialTests
{
    [Fact]
    [Trait("Category", "HostedAltLayerPhysicalDifferentialE2E")]
    public async Task AM_physical_AltSpace_release_matches_both_pinned_legacy_oracles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var compiled = new AltLayerOracleRunner();
        var source = new HostedAutoHotkeySourceRunner(() => new AltLayerOracleRunner());
        if (!compiled.IsAvailable || !source.IsAvailable)
            return;

        var scenario = new CompatibilityScenario
        {
            Id = "layer-am-physical-alt-space",
            InitialState = new ScenarioInitialState
            {
                Mode = "R",
                Ime = "off",
                Layers = [],
                Modifiers = []
            },
            Input =
            [
                Down("ALT", 10),
                Down("KANA", 20), Up("KANA", 30),
                Up("ALT", 40),
                Down("NONCONVERT", 50),
                Down("ALT", 60),
                Down("SPACE", 70),
                Up("ALT", 80),
                Up("SPACE", 90),
                Up("NONCONVERT", 100)
            ],
            Expected = new ScenarioExpected(),
            Tags = ["legacy", "layer-trigger", "alt-space"],
            RequiredEnvironment = ["hosted-windows"],
            OracleTargets = ["compiled-exe", "ahk-source", "ikeyd-runtime"]
        };

        var ikeyd = await new IKeydRuntimeScenarioRunner().RunAsync(scenario);
        var exe = await compiled.RunAsync(scenario);
        var ahk = await source.RunAsync(scenario);

        var ikeydEvents = CanonicalEvents(ikeyd.Events);
        var exeEvents = CanonicalEvents(exe.Events);
        var ahkEvents = CanonicalEvents(ahk.Events);

        Assert.True(
            exeEvents.SequenceEqual(ahkEvents, StringComparer.Ordinal),
            $"Pinned legacy EXE/AHK disagree. EXE=[{string.Join(", ", exeEvents)}], AHK=[{string.Join(", ", ahkEvents)}]");
        Assert.True(
            ikeydEvents.SequenceEqual(exeEvents, StringComparer.Ordinal),
            $"iKeyd differs from legacy. iKeyd=[{string.Join(", ", ikeydEvents)}], legacy=[{string.Join(", ", exeEvents)}]");
    }

    private static ScenarioInputEvent Down(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyDown", Key = key };

    private static ScenarioInputEvent Up(string key, long atMs)
        => new() { AtMs = atMs, Kind = "keyUp", Key = key };

    private static string[] CanonicalEvents(IReadOnlyList<ObservedKeyEvent> events)
        => events.Select(item => $"{item.Kind}:{CanonicalKey(item.Key)}").ToArray();

    private static string CanonicalKey(string key)
        => key.ToUpperInvariant() switch
        {
            "VK_A0" or "VK_A1" or "SHIFT" => "Shift",
            "VK_A2" or "VK_A3" or "CONTROL" or "CTRL" => "Control",
            "VK_A4" or "VK_A5" or "ALT" => "Alt",
            "VK_1C" or "CONVERT" or "HENKAN" => "Convert",
            "VK_1D" or "NONCONVERT" or "MUHENKAN" => "NonConvert",
            _ => key
        };

    private sealed class AltLayerOracleRunner : ICompatibilityScenarioRunner
    {
        private const nuint ForeignMarker = (nuint)0x414C5452U; // "ALTR"
        private const byte VkKana = 0x15;
        private const byte VkConvert = 0x1C;
        private const byte VkNonConvert = 0x1D;
        private const byte VkSpace = 0x20;
        private const byte VkAlt = 0x12;
        private const byte Vk3 = 0x33;
        private const byte ScanKana = 0x70;
        private const byte ScanConvert = 0x79;
        private const byte ScanNonConvert = 0x7B;
        private const byte ScanSpace = 0x39;
        private const byte ScanAlt = 0x38;
        private const uint KeyEventKeyUp = 0x0002;

        public string Name => "hotkeySKG physical Alt-layer oracle";

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
            var executable = ResolveExecutable();
            VerifySha256(executable);

            using var process = Start(executable);
            try
            {
                await Task.Delay(750, cancellationToken);
                if (process.HasExited)
                    throw new InvalidOperationException($"Legacy process exited during startup with code {process.ExitCode}.");

                // Use the same known bootstrap as the existing layer oracle. The
                // fstate/layer path being tested is independent of normal S/K routing.
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
                    Metadata = new Dictionary<string, string> { ["scope"] = "legacy-alt-layer-physical" }
                };
            }
            finally
            {
                Stop(process);
            }
        }

        private static void SendInput(ScenarioInputEvent input)
        {
            var (virtualKey, scanCode) = Resolve(input.Key ?? string.Empty);
            Send(virtualKey, scanCode,
                string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase));
        }

        private static (byte VirtualKey, byte ScanCode) Resolve(string key)
            => key.Trim().ToUpperInvariant() switch
            {
                "KANA" => (VkKana, ScanKana),
                "CONVERT" or "HENKAN" => (VkConvert, ScanConvert),
                "NONCONVERT" or "MUHENKAN" => (VkNonConvert, ScanNonConvert),
                "SPACE" => (VkSpace, ScanSpace),
                "ALT" => (VkAlt, ScanAlt),
                _ => throw new NotSupportedException($"Unsupported Alt-layer oracle input '{key}'.")
            };

        private static void Send(byte virtualKey, byte scanCode, bool keyUp)
            => NativeMethods.keybd_event(virtualKey, scanCode, keyUp ? KeyEventKeyUp : 0, ForeignMarker);

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

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Legacy executable SHA-256 mismatch. Expected {expected}, actual {actual}.");
        }

        private static Process Start(string executable)
            => Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Could not start the legacy executable.");

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
                get { lock (_gate) return _events.ToArray(); }
            }

            public void Start()
            {
                _thread = new Thread(ThreadMain) { IsBackground = true, Name = "iKeyd.AltLayerCapture" };
                _thread.Start();
                _started.Wait();
                if (_startError is not null)
                    throw new InvalidOperationException("Could not start Alt-layer capture.", _startError);
            }

            public void Clear()
            {
                lock (_gate) _events.Clear();
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
                => virtualKey switch
                {
                    0x10 or 0xA0 or 0xA1 => "Shift",
                    0x11 or 0xA2 or 0xA3 => "Control",
                    0x12 or 0xA4 or 0xA5 => "Alt",
                    0x1B => "Escape",
                    0x1C => "Convert",
                    0x1D => "NonConvert",
                    0x20 => "Space",
                    _ when virtualKey is >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
                    _ => $"VK_{virtualKey:X2}"
                };
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
