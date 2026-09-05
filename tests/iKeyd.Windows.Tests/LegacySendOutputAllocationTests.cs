using iKeyd.App;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LegacySendOutputAllocationTests
{
    [Fact]
    public void Compiled_mapping_output_fast_paths_allocate_zero_bytes_after_warmup()
    {
        var keyboard = new CountingKeyboardOutput();
        var output = new LegacySendOutput(keyboard);

        for (var i = 0; i < 100; i++)
            Exercise(output);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            Exercise(output);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
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
