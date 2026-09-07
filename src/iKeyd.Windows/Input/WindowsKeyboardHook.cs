using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Input;

public sealed class WindowsKeyboardHook : IKeyboardInputSource, IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmQuit = 0x0012;
    private const uint WmTimer = 0x0113;
    private const uint WmBehaviorDeadlineChanged = 0x8001;
    private const uint PmNoRemove = 0x0000;

    private readonly object _lifecycleGate = new();
    private readonly object _deadlineGate = new();
    private readonly KeyboardState _state;
    private readonly HookProc _hookProc;

    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hookHandle;
    private nuint _behaviorTimerId;
    private IKeyboardEventHandler? _handler;
    private Action<long>? _behaviorDeadlineCallback;
    private long? _behaviorDeadlineMs;
    private Exception? _startError;
    private ManualResetEventSlim? _started;
    private bool _disposed;

    public WindowsKeyboardHook(KeyboardState? state = null)
    {
        _state = state ?? new KeyboardState();
        _hookProc = HookCallback;
    }

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleGate)
                return _hookThread is not null && _hookHandle != 0;
        }
    }

    public KeyboardState State => _state;

    public void Start(IKeyboardEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ManualResetEventSlim started;
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hookThread is not null)
                throw new InvalidOperationException("Keyboard hook is already running.");

            _handler = handler;
            _startError = null;
            started = new ManualResetEventSlim(false);
            _started = started;
            _hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "iKeyd.Windows.KeyboardHook"
            };
            _hookThread.Start();
        }

        started.Wait();

        Exception? error;
        lock (_lifecycleGate)
        {
            error = _startError;
            if (ReferenceEquals(_started, started))
                _started = null;
        }
        started.Dispose();

        if (error is not null)
        {
            StopAfterFailedStart();
            throw new InvalidOperationException("Could not start Windows keyboard hook.", error);
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;

        lock (_lifecycleGate)
        {
            thread = _hookThread;
            threadId = _hookThreadId;
        }

        if (thread is null)
            return;

        if (threadId != 0 && !NativeMethods.PostThreadMessageW(threadId, WmQuit, 0, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not stop keyboard hook thread.");

        if (!ReferenceEquals(Thread.CurrentThread, thread))
            thread.Join();
    }

    /// <summary>
    /// Schedules one absolute Environment.TickCount64 deadline on the keyboard
    /// hook's own Windows message queue. The callback therefore cannot overtake a
    /// physical input notification that is already queued ahead of the timer.
    /// </summary>
    public void ScheduleBehaviorDeadline(long? deadlineMs, Action<long> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_deadlineGate)
        {
            _behaviorDeadlineMs = deadlineMs;
            _behaviorDeadlineCallback = deadlineMs is null ? null : callback;
        }

        uint threadId;
        lock (_lifecycleGate)
            threadId = _hookThreadId;

        if (threadId == 0)
            return;

        // State is updated before posting. Even if an old WM_TIMER is already
        // queued, its handler will re-check the current deadline before firing.
        _ = NativeMethods.PostThreadMessageW(threadId, WmBehaviorDeadlineChanged, 0, 0);
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        try
        {
            Stop();
        }
        finally
        {
            ClearBehaviorDeadline();
            _state.Clear();
            GC.SuppressFinalize(this);
        }
    }

    private void HookThreadMain()
    {
        nint hook = 0;
        try
        {
            var threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessageW(out _, 0, 0, 0, PmNoRemove);

            var module = NativeMethods.GetModuleHandleW(null);
            hook = NativeMethods.SetWindowsHookExW(WhKeyboardLl, _hookProc, module, 0);
            if (hook == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install WH_KEYBOARD_LL hook.");

            lock (_lifecycleGate)
            {
                _hookThreadId = threadId;
                _hookHandle = hook;
            }
            _started?.Set();

            while (true)
            {
                var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                if (result == 0)
                    break;
                if (result == -1)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Keyboard hook message loop failed.");

                if (message.Message == WmBehaviorDeadlineChanged)
                {
                    RearmBehaviorDeadlineTimerOnHookThread();
                    continue;
                }

                if (message.Message == WmTimer &&
                    _behaviorTimerId != 0 &&
                    message.WParam == _behaviorTimerId)
                {
                    HandleBehaviorDeadlineTimerOnHookThread();
                    continue;
                }

                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessageW(ref message);
            }
        }
        catch (Exception error)
        {
            lock (_lifecycleGate)
                _startError ??= error;
            _started?.Set();
        }
        finally
        {
            KillBehaviorDeadlineTimerOnHookThread();
            ClearBehaviorDeadline();

            if (hook != 0)
                NativeMethods.UnhookWindowsHookEx(hook);

            IKeyboardEventHandler? handler;
            lock (_lifecycleGate)
            {
                handler = _handler;
                _hookHandle = 0;
                _hookThreadId = 0;
                _hookThread = null;
                _handler = null;
                _started = null;
            }

            TryResetHandler(handler);
            _state.Clear();
        }
    }

    private void RearmBehaviorDeadlineTimerOnHookThread()
    {
        KillBehaviorDeadlineTimerOnHookThread();

        long? deadline;
        lock (_deadlineGate)
            deadline = _behaviorDeadlineMs;

        if (deadline is null)
            return;

        var remainingMs = Math.Max(1L, deadline.Value - Environment.TickCount64);
        var dueMs = checked((uint)Math.Min(remainingMs, uint.MaxValue));
        _behaviorTimerId = NativeMethods.SetTimer(0, 0, dueMs, 0);
        if (_behaviorTimerId == 0)
        {
            Trace.WriteLine(
                $"iKeyd could not schedule Behavior deadline timer: Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private void HandleBehaviorDeadlineTimerOnHookThread()
    {
        KillBehaviorDeadlineTimerOnHookThread();

        Action<long>? callback;
        long? deadline;
        var now = Environment.TickCount64;
        lock (_deadlineGate)
        {
            deadline = _behaviorDeadlineMs;
            if (deadline is null)
                return;

            if (now < deadline.Value)
            {
                callback = null;
            }
            else
            {
                callback = _behaviorDeadlineCallback;
                _behaviorDeadlineMs = null;
                _behaviorDeadlineCallback = null;
            }
        }

        if (now < deadline.Value)
        {
            // A stale WM_TIMER can survive cancellation/replacement in the queue.
            // Re-arm from the current absolute deadline instead of firing early.
            RearmBehaviorDeadlineTimerOnHookThread();
            return;
        }

        if (callback is null)
            return;

        try
        {
            callback(now);
        }
        catch
        {
            // Deadline execution is part of the same stateful input pipeline. Keep
            // the hook alive and apply the same fail-open reset discipline used by
            // physical hook callbacks.
            TryResetHandler(_handler);
        }
    }

    private void KillBehaviorDeadlineTimerOnHookThread()
    {
        if (_behaviorTimerId == 0)
            return;

        _ = NativeMethods.KillTimer(0, _behaviorTimerId);
        _behaviorTimerId = 0;
    }

    private void ClearBehaviorDeadline()
    {
        lock (_deadlineGate)
        {
            _behaviorDeadlineMs = null;
            _behaviorDeadlineCallback = null;
        }
    }

    private void StopAfterFailedStart()
    {
        Thread? thread;
        lock (_lifecycleGate)
            thread = _hookThread;
        thread?.Join();
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        var handler = _handler;
        if (code < 0 || handler is null)
            return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);

        try
        {
            var native = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var timestampMs = WindowsKeyboardEventNormalizer.ExpandNativeTimestamp(
                native.Time,
                Environment.TickCount64);
            var keyboardEvent = WindowsKeyboardEventNormalizer.Normalize(
                native.VirtualKey,
                native.ScanCode,
                native.Flags,
                native.ExtraInfo,
                timestampMs);

            if (keyboardEvent.Origin == KeyEventOrigin.OwnInjected)
                return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);

            _state.Apply(keyboardEvent);
            return handler.OnKeyboardEvent(keyboardEvent) == KeyboardDisposition.Suppress
                ? 1
                : NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
        }
        catch
        {
            // A stateful handler can fail after mutating only half of a transition.
            // Do not leave a stuck layer/modifier behind just because the hook must
            // fail open for this event.
            TryResetHandler(handler);
            return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
        }
    }

    private static void TryResetHandler(IKeyboardEventHandler? handler)
    {
        if (handler is not IInputStateResettable resettable)
            return;

        try
        {
            resettable.ResetInputState();
        }
        catch
        {
            // Recovery itself must never take down the low-level hook.
        }
    }

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

    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetWindowsHookExW(int hookId, HookProc callback, nint module, uint threadId);

        [DllImport("user32.dll")]
        public static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hook);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nuint SetTimer(nint window, nuint timerId, uint elapsedMs, nint timerProc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool KillTimer(nint window, nuint timerId);

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
