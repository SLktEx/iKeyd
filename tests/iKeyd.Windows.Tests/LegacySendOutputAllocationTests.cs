using iKeyd.App;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacySendOutputAllocationTests
{
    [Fact]
    public void Compiled_mapping_output_fast_paths_allocate_zero_bytes_after_warmup()
    {
        const int MeasurementIterations = 10_000;
        const int MaxWarmupWindows = 6;
        const int RequiredStableWindows = 2;

        var keyboard = new CountingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        // A fixed small warmup can race .NET's tiered JIT/PGO promotion and count
        // one-time runtime initialization as product-path allocation. Require two
        // consecutive zero-allocation windows instead: persistent per-call
        // allocation can never satisfy this, while bounded JIT warmup can.
        for (var i = 0; i < 100; i++)
            Exercise(output);

        var stableWindows = 0;
        long lastAllocated = long.MaxValue;
        for (var attempt = 0; attempt < MaxWarmupWindows; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < MeasurementIterations; i++)
                Exercise(output);
            lastAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

            stableWindows = lastAllocated == 0 ? stableWindows + 1 : 0;
            if (stableWindows >= RequiredStableWindows)
                break;
        }

        Assert.True(
            stableWindows >= RequiredStableWindows,
            $"Fast paths did not reach steady-state zero allocation; last window allocated {lastAllocated} bytes.");
        Assert.True(keyboard.CallCount > 0);
    }

    private static void Exercise(LegacySendOutput output)
    {
        output.Send("ni");
        output.Send("{F1}");
        output.SendChord(WindowsKeyMap.Control, (ushort)'A');
        output.SendChord(WindowsKeyMap.Control, WindowsKeyMap.Shift, (ushort)'A');
    }

    private sealed class CountingKeyboardOutput : IKeyboardOutput
    {
        public int CallCount { get; private set; }

        public void SendKey(KeyboardKey key, KeyEventKind kind) => CallCount++;
        public void SendKeyPress(KeyboardKey key) => CallCount++;
        public void SendText(string text) => CallCount++;
        public bool IsToggleOn(ushort virtualKey) => false;
    }
}
