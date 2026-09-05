using System.Runtime.InteropServices;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class WindowsKeyboardE2ETests
{
    private const byte VkF24 = 0x87;
    private const uint KeyEventKeyUp = 0x0002;
    private static readonly nuint ForeignMarker = (nuint)0x13572468U;

    [Fact]
    [Trait("Category", "WindowsE2E")]
    public void Low_level_hook_receives_external_injected_input_and_ignores_own_SendInput()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var hook = new WindowsKeyboardHook();
        var handler = new RecordingHandler(VkF24, expectedEvents: 2);
        hook.Start(handler);

        try
        {
            // Exercise the real user32 -> WH_KEYBOARD_LL path with injected input that
            // does not carry iKeyd's marker. The hook should observe and suppress it.
            NativeMethods.keybd_event(VkF24, 0, 0, ForeignMarker);
            NativeMethods.keybd_event(VkF24, 0, KeyEventKeyUp, ForeignMarker);

            Assert.True(handler.Wait(TimeSpan.FromSeconds(5)), "WH_KEYBOARD_LL did not receive the injected F24 events.");

            var events = handler.Snapshot();
            Assert.Equal(2, events.Count);
            Assert.Equal(KeyEventKind.Down, events[0].Kind);
            Assert.Equal(KeyEventKind.Up, events[1].Kind);
            Assert.All(events, e => Assert.Equal(KeyEventOrigin.Injected, e.Origin));
            Assert.All(events, e => Assert.Equal(VkF24, e.Key.VirtualKey));

            // Now use iKeyd's actual SendInput implementation. These events travel
            // through Windows too, but carry InjectionMarker and must not be sent
            // back to the application handler.
            var beforeOwnInjection = handler.Count;
            var output = new WindowsKeyboardOutput();
            output.SendKeyPress(new KeyboardKey(VkF24, 0));

            Thread.Sleep(300);
            Assert.Equal(beforeOwnInjection, handler.Count);
        }
        finally
        {
            hook.Stop();
        }
    }

    private sealed class RecordingHandler : IKeyboardEventHandler
    {
        private readonly ushort _virtualKey;
        private readonly int _expectedEvents;
        private readonly ManualResetEventSlim _received = new(false);
        private readonly object _gate = new();
        private readonly List<KeyboardEvent> _events = [];

        public RecordingHandler(ushort virtualKey, int expectedEvents)
        {
            _virtualKey = virtualKey;
            _expectedEvents = expectedEvents;
        }

        public int Count
        {
            get
            {
                lock (_gate)
                    return _events.Count;
            }
        }

        public KeyboardDisposition OnKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            if (keyboardEvent.Key.VirtualKey != _virtualKey)
                return KeyboardDisposition.PassThrough;

            lock (_gate)
            {
                _events.Add(keyboardEvent);
                if (_events.Count >= _expectedEvents)
                    _received.Set();
            }

            return KeyboardDisposition.Suppress;
        }

        public bool Wait(TimeSpan timeout) => _received.Wait(timeout);

        public IReadOnlyList<KeyboardEvent> Snapshot()
        {
            lock (_gate)
                return _events.ToArray();
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
