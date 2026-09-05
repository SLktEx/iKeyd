using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using iKeyd.Compatibility.Tests;
using iKeyd.Core.Chords;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;

namespace iKeyd.Windows.Tests;

public sealed class WindowsScenarioRunner : ICompatibilityScenarioRunner
{
    private static readonly nuint ForeignMarker = (nuint)0x13572468U;

    public string Name => "iKeyd.Windows";
    public bool IsAvailable => OperatingSystem.IsWindows();

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows scenario runner requires Windows.");
        if (scenario.InitialState.Modifiers.Count != 0)
            throw new NotSupportedException("The Windows scenario runner does not apply initial modifiers yet.");

        using var backend = new WindowsKeyboardBackend();
        using var capture = new OwnInjectedUnicodeCapture();
        var handler = new CoreScenarioHandler(
            LegacyKeymapLoader.Load(scenario.InitialState.Mode),
            backend,
            scenario.Input.Count);

        backend.Start(handler);
        capture.Start();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            foreach (var input in scenario.Input)
            {
                await DelayUntilAsync(stopwatch, input.AtMs, cancellationToken);
                ExternalKeyboardInjector.Send(input, ForeignMarker);
            }

            if (!handler.WaitForInputs(TimeSpan.FromSeconds(5)))
                throw new TimeoutException(
                    $"Windows hook received {handler.InputCount} of {scenario.Input.Count} scenario events.");

            handler.Flush();

            var expectedLength = scenario.Expected.Text?.Length ?? 0;
            if (!capture.WaitForLength(expectedLength, TimeSpan.FromSeconds(5)))
                throw new TimeoutException(
                    $"Windows SendInput capture received '{capture.Text}' while waiting for {expectedLength} UTF-16 code units.");

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
                    ["scope"] = "windows-hook-core-sendinput"
                }
            };
        }
        finally
        {
            capture.Stop();
            backend.Stop();
        }
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

    private sealed class CoreScenarioHandler : IKeyboardEventHandler
    {
        private readonly ChordEngine<string> _engine;
        private readonly IKeyboardOutput _output;
        private readonly int _expectedInputs;
        private readonly ManualResetEventSlim _received = new(false);
        private readonly object _gate = new();
        private int _inputCount;

        public CoreScenarioHandler(
            iKeyd.Core.Keymaps.Keymap<string> keymap,
            IKeyboardOutput output,
            int expectedInputs)
        {
            _engine = new ChordEngine<string>(keymap);
            _output = output;
            _expectedInputs = expectedInputs;
        }

        public int InputCount
        {
            get
            {
                lock (_gate)
                    return _inputCount;
            }
        }

        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            var keyId = VirtualKeyNames.TryResolve(keyboardEvent.Key.VirtualKey);
            if (keyId is null)
                return KeyboardDisposition.PassThrough;

            lock (_gate)
            {
                if (keyboardEvent.Kind == KeyEventKind.Down)
                {
                    Send(_engine.AdvanceTo(keyboardEvent.TimestampMs));
                    Send(_engine.OnKeyDown(keyId.Value, keyboardEvent.TimestampMs));
                }

                _inputCount++;
                if (_inputCount >= _expectedInputs)
                    _received.Set();
            }

            return KeyboardDisposition.Suppress;
        }

        public bool WaitForInputs(TimeSpan timeout) => _received.Wait(timeout);

        public void Flush()
        {
            lock (_gate)
                Send(_engine.Flush());
        }

        private void Send(IReadOnlyList<string> outputs)
        {
            foreach (var output in outputs)
                _output.SendText(output);
        }
    }

    private static class VirtualKeyNames
    {
        public static KeyId? TryResolve(ushort virtualKey)
        {
            if (virtualKey is >= 0x41 and <= 0x5A)
                return new KeyId(((char)virtualKey).ToString());
            if (virtualKey is >= 0x30 and <= 0x39)
                return new KeyId(((char)virtualKey).ToString());

            return virtualKey switch
            {
                0xBA => new KeyId("SColon"),
                0xBC => new KeyId("Comma"),
                0xBE => new KeyId("Dot"),
                0xBF => new KeyId("Slash"),
                _ => null
            };
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

    private sealed class OwnInjectedUnicodeCapture : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const uint WmQuit = 0x0012;
        private const uint WmKeyDown = 0x0100;
        private const uint WmSysKeyDown = 0x0104;
        private const uint PmNoRemove = 0x0000;
        private const uint VkPacket = 0xE7;

        private readonly object _gate = new();
        private readonly HookProc _hookProc;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly StringBuilder _text = new();
        private Thread? _thread;
        private uint _threadId;
        private nint _hook;
        private Exception? _startError;

        public OwnInjectedUnicodeCapture()
        {
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
                Name = "iKeyd.Windows.Tests.UnicodeCapture"
            };
            _thread.Start();
            _started.Wait();

            if (_startError is not null)
                throw new InvalidOperationException("Could not start Unicode capture hook.", _startError);
        }

        public void Stop()
        {
            var thread = _thread;
            if (thread is null)
                return;

            if (_threadId != 0 && !NativeMethods.PostThreadMessageW(_threadId, WmQuit, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not stop Unicode capture hook thread.");

            if (!ReferenceEquals(Thread.CurrentThread, thread))
                thread.Join();
            _thread = null;
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
            Stop();
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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install Unicode capture hook.");

                _started.Set();

                while (true)
                {
                    var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                    if (result == 0)
                        break;
                    if (result == -1)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Unicode capture message loop failed.");

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
                if (native.ExtraInfo == WindowsKeyboardOutput.InjectionMarker &&
                    native.ScanCode != 0 &&
                    (native.VirtualKey == VkPacket || native.VirtualKey == 0))
                {
                    lock (_gate)
                        _text.Append((char)native.ScanCode);
                }
            }

            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
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

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);
        }
    }
}
