using System.ComponentModel;
using System.Runtime.InteropServices;
using iKeyd.Compatibility.Tests;

namespace iKeyd.Windows.Tests;

/// <summary>
/// Decorates a real legacy-process runner and records the keyboard events that
/// the legacy process injects. The physical scenario input and the hosted T-mode
/// control chords use the pinned foreign marker and are excluded, leaving only
/// observable legacy output events.
/// </summary>
public sealed class RuntimeEventCaptureRunner : ICompatibilityScenarioRunner
{
    private const nuint ForeignMarker = (nuint)0x24681357U;
    private readonly ICompatibilityScenarioRunner _inner;

    public RuntimeEventCaptureRunner(ICompatibilityScenarioRunner inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public string Name => _inner.Name + " + runtime-events";
    public bool IsAvailable => _inner.IsAvailable;

    public async Task<ScenarioRunResult> RunAsync(
        CompatibilityScenario scenario,
        CancellationToken cancellationToken = default)
    {
        using var capture = new InjectedEventCapture(ForeignMarker);
        capture.Start();
        var result = await _inner.RunAsync(scenario, cancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        return result with
        {
            Runner = Name,
            Events = capture.Events.ToList(),
            Text = string.Empty,
            Metadata = new Dictionary<string, string>(result.Metadata)
            {
                ["eventCapture"] = "legacy-injected-keyboard-events",
                ["legacyTextDiagnostic"] = result.Text ?? string.Empty
            }
        };
    }

    private sealed class InjectedEventCapture : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const uint WmQuit = 0x0012;
        private const uint WmKeyDown = 0x0100;
        private const uint WmKeyUp = 0x0101;
        private const uint WmSysKeyDown = 0x0104;
        private const uint WmSysKeyUp = 0x0105;
        private const uint PmNoRemove = 0x0000;
        private const uint LlkhfInjected = 0x00000010;

        private readonly nuint _foreignMarker;
        private readonly object _gate = new();
        private readonly HookProc _hookProc;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly List<ObservedKeyEvent> _events = [];
        private Thread? _thread;
        private uint _threadId;
        private nint _hook;
        private Exception? _startError;

        public InjectedEventCapture(nuint foreignMarker)
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
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "iKeyd.Legacy.RuntimeEventCapture"
            };
            _thread.Start();
            _started.Wait();
            if (_startError is not null)
                throw new InvalidOperationException("Could not start legacy runtime event capture.", _startError);
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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install runtime event capture hook.");
                _started.Set();

                while (true)
                {
                    var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                    if (result == 0)
                        break;
                    if (result == -1)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Runtime event capture message loop failed.");
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
            var isKeyboardMessage = wParam == (nuint)WmKeyDown || wParam == (nuint)WmKeyUp ||
                                    wParam == (nuint)WmSysKeyDown || wParam == (nuint)WmSysKeyUp;
            if (code >= 0 && isKeyboardMessage)
            {
                var native = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if ((native.Flags & LlkhfInjected) != 0 && native.ExtraInfo != _foreignMarker)
                {
                    var kind = wParam == (nuint)WmKeyDown || wParam == (nuint)WmSysKeyDown
                        ? "keyDown"
                        : "keyUp";
                    lock (_gate)
                    {
                        _events.Add(new ObservedKeyEvent
                        {
                            Kind = kind,
                            Key = ScenarioKeyboard.ResolveName((ushort)native.VirtualKey)
                        });
                    }
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
            [DllImport("user32.dll", SetLastError = true)]
            public static extern nint SetWindowsHookExW(int hookId, HookProc callback, nint module, uint threadId);
            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(nint hook);
            [DllImport("user32.dll")]
            public static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
            [DllImport("user32.dll", SetLastError = true)]
            public static extern int GetMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage);
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool TranslateMessage(ref NativeMessage message);
            [DllImport("user32.dll")]
            public static extern nint DispatchMessageW(ref NativeMessage message);
            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PeekMessageW(out NativeMessage message, nint window, uint minMessage, uint maxMessage, uint removeMessage);
            [DllImport("kernel32.dll")]
            public static extern uint GetCurrentThreadId();
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern nint GetModuleHandleW(string? moduleName);
        }
    }
}
