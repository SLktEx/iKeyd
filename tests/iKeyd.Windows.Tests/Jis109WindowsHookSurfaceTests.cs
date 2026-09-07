using System.Diagnostics;
using System.Runtime.InteropServices;
using iKeyd.App;
using iKeyd.Core.Chords;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class Jis109WindowsHookSurfaceTests
{
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private static readonly nuint ForeignMarker = (nuint)0x174109U;

    [Fact]
    [Trait("Category", "WindowsE2E")]
    public void Low_level_hook_preserves_number_and_function_row_real_scan_identity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var expected = NumberAndFunctionBindings().ToArray();
        using var hook = new WindowsKeyboardHook();
        var handler = new SurfaceRecordingHandler(expected.Select(item => item.Code), expected.Length * 2);
        hook.Start(handler);

        try
        {
            foreach (var binding in expected)
            {
                var flags = binding.WindowsKey.IsExtended ? KeyEventExtendedKey : 0U;
                NativeMethods.keybd_event(
                    checked((byte)binding.WindowsKey.VirtualKey),
                    checked((byte)binding.WindowsKey.ScanCode),
                    flags,
                    ForeignMarker);
                NativeMethods.keybd_event(
                    checked((byte)binding.WindowsKey.VirtualKey),
                    checked((byte)binding.WindowsKey.ScanCode),
                    flags | KeyEventKeyUp,
                    ForeignMarker);
            }

            Assert.True(
                handler.Wait(TimeSpan.FromSeconds(10)),
                $"WH_KEYBOARD_LL observed only {handler.Count}/{expected.Length * 2} audited events.");

            var actual = handler.Snapshot();
            Assert.Equal(expected.Length * 2, actual.Count);

            for (var index = 0; index < expected.Length; index++)
            {
                var binding = expected[index];
                var down = actual[index * 2];
                var up = actual[index * 2 + 1];

                Assert.Equal(binding.Code, down.Code);
                Assert.Equal(binding.Code, up.Code);
                Assert.Equal(KeyEventKind.Down, down.Event.Kind);
                Assert.Equal(KeyEventKind.Up, up.Event.Kind);
                Assert.Equal(KeyEventOrigin.Injected, down.Event.Origin);
                Assert.Equal(KeyEventOrigin.Injected, up.Event.Origin);
                Assert.Equal(binding.WindowsKey.VirtualKey, down.Event.Key.VirtualKey);
                Assert.Equal(binding.WindowsKey.VirtualKey, up.Event.Key.VirtualKey);
                Assert.Equal(binding.WindowsKey.ScanCode, down.Event.Key.ScanCode);
                Assert.Equal(binding.WindowsKey.ScanCode, up.Event.Key.ScanCode);
                Assert.Equal(binding.WindowsKey.IsExtended, down.Event.Key.IsExtended);
                Assert.Equal(binding.WindowsKey.IsExtended, up.Event.Key.IsExtended);
            }
        }
        finally
        {
            hook.Stop();
        }
    }

    private static IEnumerable<WindowsPhysicalKeyBinding> NumberAndFunctionBindings()
    {
        for (var digit = 0; digit <= 9; digit++)
            yield return BindingFor((KeyCode)((int)KeyCode.Digit0 + digit));

        for (var functionNumber = 1; functionNumber <= 12; functionNumber++)
            yield return BindingFor((KeyCode)((int)KeyCode.F1 + functionNumber - 1));
    }

    private static WindowsPhysicalKeyBinding BindingFor(KeyCode code)
        => WindowsKeyMap.Jis109PhysicalBindings.Single(binding => binding.Code == code);

    private sealed class SurfaceRecordingHandler : IKeyboardEventHandler
    {
        private readonly HashSet<KeyCode> _targets;
        private readonly int _expectedEvents;
        private readonly ManualResetEventSlim _received = new(false);
        private readonly object _gate = new();
        private readonly List<ObservedSurfaceEvent> _events = [];

        public SurfaceRecordingHandler(IEnumerable<KeyCode> targets, int expectedEvents)
        {
            _targets = targets.ToHashSet();
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
            var resolved = WindowsKeyMap.TryResolveKeyId(keyboardEvent.Key);
            if (resolved is null || !_targets.Contains(resolved.Value.Code))
                return KeyboardDisposition.PassThrough;

            lock (_gate)
            {
                _events.Add(new ObservedSurfaceEvent(resolved.Value.Code, keyboardEvent));
                if (_events.Count >= _expectedEvents)
                    _received.Set();
            }

            // Never let verification input escape into the runner's foreground app.
            return KeyboardDisposition.Suppress;
        }

        public bool Wait(TimeSpan timeout) => _received.Wait(timeout);

        public IReadOnlyList<ObservedSurfaceEvent> Snapshot()
        {
            lock (_gate)
                return _events.ToArray();
        }
    }

    private readonly record struct ObservedSurfaceEvent(KeyCode Code, KeyboardEvent Event);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
