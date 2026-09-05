using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

public sealed class LegacyExecutableScenarioRunner : ICompatibilityScenarioRunner
{
    public const string ExecutableEnvironmentVariable = "IKEYD_LEGACY_EXE";
    public const string ExpectedSha256EnvironmentVariable = "IKEYD_LEGACY_EXE_SHA256";
    public const string ReferenceSha256 = "5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea";

    private static readonly nuint ForeignMarker = (nuint)0x24681357U;

    public string Name => "hotkeySKG.exe";

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;

            var path = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
    }

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The legacy executable runner requires Windows.");
        if (scenario.InitialState.Modifiers.Count != 0)
            throw new NotSupportedException("The legacy executable runner does not apply initial modifiers yet.");
        if (!string.Equals(scenario.InitialState.Mode, "S", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The first legacy executable runner supports only the default S mode.");

        var executable = ResolveExecutable();
        var sha256 = ComputeSha256(executable);
        var expectedSha256 = Environment.GetEnvironmentVariable(ExpectedSha256EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(expectedSha256))
            expectedSha256 = ReferenceSha256;

        if (!string.Equals(sha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Legacy executable SHA-256 mismatch. Expected {expectedSha256}, actual {sha256}. " +
                $"Set {ExpectedSha256EnvironmentVariable} explicitly when intentionally testing another binary.");
        }

        using var sink = new LegacyImeSink();
        sink.Start();
        sink.Activate(scenario.InitialState.Ime);

        using var process = StartLegacyExecutable(executable);
        try
        {
            await WaitForLegacyStartupAsync(process, cancellationToken);
            sink.Activate(scenario.InitialState.Ime);

            if (string.Equals(scenario.InitialState.Ime, "on", StringComparison.OrdinalIgnoreCase) &&
                !sink.IsRomaKanaActive())
            {
                throw new InvalidOperationException(
                    "The test sink could not enter an IME state accepted by legacy IME_IfRomaKana(). " +
                    "Run this test in a Windows session with a Japanese IME installed and enabled.");
            }

            using var capture = new LegacyInjectedOutputCapture(ForeignMarker);
            capture.Start();

            var stopwatch = Stopwatch.StartNew();
            foreach (var input in scenario.Input)
            {
                await DelayUntilAsync(stopwatch, input.AtMs, cancellationToken);
                ExternalKeyboardInjector.Send(input, ForeignMarker);
            }

            var expectedLength = scenario.Expected.Text?.Length ?? 0;
            if (!capture.WaitForLength(expectedLength, TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException(
                    $"Legacy executable emitted '{capture.Text}' while waiting for {expectedLength} output characters. " +
                    "If no output was observed, verify that the active Japanese IME accepts the injected test input.");
            }

            return new ScenarioRunResult
            {
                Runner = Name,
                ScenarioId = scenario.Id,
                Text = capture.Text,
                Events = [],
                Metadata = new Dictionary<string, string>
                {
                    ["mode"] = scenario.InitialState.Mode,
                    ["ime"] = scenario.InitialState.Ime,
                    ["scope"] = "legacy-process-hook-output",
                    ["sha256"] = sha256
                }
            };
        }
        finally
        {
            StopProcess(process);
        }
    }

    private static string ResolveExecutable()
    {
        var path = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"Set {ExecutableEnvironmentVariable} to the local path of hotkeySKG.exe before running legacy executable tests.");
        }

        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Legacy hotkeySKG executable was not found.", path);
        return path;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static Process StartLegacyExecutable(string executable)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        });

        return process ?? throw new InvalidOperationException("Could not start the legacy hotkeySKG executable.");
    }

    private static async Task WaitForLegacyStartupAsync(Process process, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        if (process.HasExited)
            throw new InvalidOperationException($"Legacy executable exited during startup with code {process.ExitCode}.");
    }

    private static async Task DelayUntilAsync(
        Stopwatch stopwatch,
        long targetMs,
        CancellationToken cancellationToken)
    {
        var remaining = targetMs - stopwatch.ElapsedMilliseconds;
        if (remaining > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(remaining), cancellationToken);
    }

    private static void StopProcess(Process process)
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
            // Test cleanup must not mask the comparison failure.
        }
    }

    private static class ExternalKeyboardInjector
    {
        private const uint KeyEventKeyUp = 0x0002;

        public static void Send(ScenarioInputEvent input, nuint marker)
        {
            var virtualKey = ResolveVirtualKey(input.Key!);
            var flags = string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase)
                ? KeyEventKeyUp
                : 0u;
            NativeMethods.keybd_event(virtualKey, 0, flags, marker);
        }

        private static byte ResolveVirtualKey(string key)
        {
            if (key.Length == 1)
            {
                var ch = char.ToUpperInvariant(key[0]);
                if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
                    return (byte)ch;
            }

            return key.ToUpperInvariant() switch
            {
                "SCOLON" => 0xBA,
                "COMMA" => 0xBC,
                "DOT" => 0xBE,
                "SLASH" => 0xBF,
                _ => throw new NotSupportedException($"No Windows virtual-key mapping for scenario key '{key}'.")
            };
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
        }
    }

    private sealed class LegacyImeSink : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new(false);
        private Thread? _thread;
        private Form? _form;
        private TextBox? _textBox;
        private Exception? _startError;

        public void Start()
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "iKeyd.LegacyExe.ImeSink"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();

            if (_startError is not null)
                throw new InvalidOperationException("Could not start legacy IME test sink.", _startError);
        }

        public void Activate(string imeState)
        {
            Invoke(() =>
            {
                var form = _form!;
                var textBox = _textBox!;
                textBox.ImeMode = imeState.ToLowerInvariant() switch
                {
                    "on" => ImeMode.Hiragana,
                    "off" => ImeMode.Off,
                    _ => textBox.ImeMode
                };
                form.Show();
                form.WindowState = FormWindowState.Normal;
                form.BringToFront();
                form.Activate();
                NativeMethods.SetForegroundWindow(form.Handle);
                textBox.Focus();
            });
        }

        public bool IsRomaKanaActive()
            => Invoke(() => LegacyImeProbe.IsRomaKana(_textBox!.Handle));

        public void Dispose()
        {
            try
            {
                if (_form is not null && !_form.IsDisposed)
                    Invoke(() => _form!.Close());
            }
            catch
            {
                // Best-effort cleanup.
            }

            _thread?.Join(TimeSpan.FromSeconds(5));
            _ready.Dispose();
        }

        private void ThreadMain()
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                _form = new Form
                {
                    Text = $"iKeyd Legacy Compatibility Sink {Guid.NewGuid():N}",
                    Width = 640,
                    Height = 240,
                    TopMost = true,
                    ShowInTaskbar = true
                };
                _textBox = new TextBox
                {
                    Multiline = true,
                    Dock = DockStyle.Fill,
                    ImeMode = ImeMode.Hiragana
                };
                _form.Controls.Add(_textBox);
                _form.Shown += (_, _) =>
                {
                    _textBox.Focus();
                    _ready.Set();
                };

                Application.Run(_form);
            }
            catch (Exception error)
            {
                _startError = error;
                _ready.Set();
            }
        }

        private void Invoke(Action action)
        {
            var form = _form ?? throw new InvalidOperationException("Legacy IME sink is not running.");
            if (form.InvokeRequired)
                form.Invoke(action);
            else
                action();
        }

        private T Invoke<T>(Func<T> action)
        {
            var form = _form ?? throw new InvalidOperationException("Legacy IME sink is not running.");
            return form.InvokeRequired ? (T)form.Invoke(action) : action();
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(nint window);
        }
    }

    private static class LegacyImeProbe
    {
        private const uint WmImeControl = 0x0283;
        private const nuint ImcGetConversionMode = 0x0001;
        private const nuint ImcGetOpenStatus = 0x0005;

        public static bool IsRomaKana(nint window)
        {
            var imeWindow = NativeMethods.ImmGetDefaultIMEWnd(window);
            if (imeWindow == 0)
                return false;

            var open = NativeMethods.SendMessageW(imeWindow, WmImeControl, ImcGetOpenStatus, 0);
            if (open != 1)
                return false;

            var conversion = (int)NativeMethods.SendMessageW(imeWindow, WmImeControl, ImcGetConversionMode, 0);
            return conversion is 9 or 19 or 25 or 27 or 16;
        }

        private static class NativeMethods
        {
            [DllImport("imm32.dll")]
            public static extern nint ImmGetDefaultIMEWnd(nint window);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
        }
    }

    private sealed class LegacyInjectedOutputCapture : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const uint WmQuit = 0x0012;
        private const uint WmKeyDown = 0x0100;
        private const uint WmSysKeyDown = 0x0104;
        private const uint PmNoRemove = 0x0000;
        private const uint LlkhfInjected = 0x00000010;

        private readonly nuint _foreignMarker;
        private readonly object _gate = new();
        private readonly HookProc _hookProc;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly StringBuilder _text = new();
        private Thread? _thread;
        private uint _threadId;
        private nint _hook;
        private Exception? _startError;

        public LegacyInjectedOutputCapture(nuint foreignMarker)
        {
            _foreignMarker = foreignMarker;
            _hookProc = HookCallback;
        }

        public string Text
        {
            get
            {
                lock (_gate)
                    return _text.ToString();
            }
        }

        public void Start()
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "iKeyd.LegacyExe.OutputCapture"
            };
            _thread.Start();
            _started.Wait();

            if (_startError is not null)
                throw new InvalidOperationException("Could not start legacy output capture hook.", _startError);
        }

        public bool WaitForLength(int length, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (Text.Length >= length)
                    return true;
                Thread.Sleep(10);
            }

            return Text.Length >= length;
        }

        public void Dispose()
        {
            var thread = _thread;
            if (thread is not null)
            {
                if (_threadId != 0)
                    NativeMethods.PostThreadMessageW(_threadId, WmQuit, 0, 0);
                if (!ReferenceEquals(Thread.CurrentThread, thread))
                    thread.Join(TimeSpan.FromSeconds(5));
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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install legacy output capture hook.");

                _started.Set();

                while (true)
                {
                    var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                    if (result == 0)
                        break;
                    if (result == -1)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Legacy output capture message loop failed.");

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
            if (code >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
            {
                var native = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if ((native.Flags & LlkhfInjected) != 0 && native.ExtraInfo != _foreignMarker)
                {
                    var character = TryTranslate(native.VirtualKey);
                    if (character is not null)
                    {
                        lock (_gate)
                            _text.Append(character.Value);
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        private static char? TryTranslate(uint virtualKey)
        {
            if (virtualKey is >= 0x41 and <= 0x5A)
                return char.ToLowerInvariant((char)virtualKey);
            if (virtualKey is >= 0x30 and <= 0x39)
                return (char)virtualKey;

            return virtualKey switch
            {
                0xBD => '-',
                0xBF => '/',
                0xBC => ',',
                0xBE => '.',
                0xDB => '[',
                0xDD => ']',
                _ => null
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
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

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

            [DllImport("user32.dll")]
            public static extern int GetMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool TranslateMessage(ref NativeMessage message);

            [DllImport("user32.dll")]
            public static extern nint DispatchMessageW(ref NativeMessage message);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PeekMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage, uint removeMessage);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);
        }
    }
}
