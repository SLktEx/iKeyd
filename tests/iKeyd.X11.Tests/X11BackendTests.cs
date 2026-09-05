using iKeyd.Core.Platform;
using iKeyd.X11.Clipboard;
using Xunit;

namespace iKeyd.X11.Tests;

public sealed class X11BackendTests
{
    [Fact]
    public void Probe_does_not_claim_X11_or_clipboard_without_display()
    {
        var options = new X11BackendOptions([], UInputPath: "/definitely/missing", DisplayName: string.Empty, XclipCommand: "definitely-missing-xclip");
        var probe = X11BackendProbe.Probe(options);

        Assert.False(probe.HasDisplay);
        Assert.False(probe.Capabilities.Supports(BackendCapability.WindowQuery));
        Assert.False(probe.Capabilities.Supports(BackendCapability.ClipboardRead));
    }

    [Fact]
    public void Clipboard_adapter_reads_writes_and_watches_selection()
    {
        var runner = new FakeRunner { Current = "one" };
        var options = new X11BackendOptions([], DisplayName: ":fake", XclipCommand: "xclip-fake");
        using var clipboard = new X11ClipboardService(options, runner, TimeSpan.FromMilliseconds(10), hasDisplay: true);
        using var changed = new ManualResetEventSlim();
        clipboard.Changed += (_, _) => changed.Set();

        Assert.Equal("one", clipboard.ReadText());
        clipboard.WriteText("two");
        Assert.Equal("two", runner.Current);

        runner.Current = "three";
        Assert.True(changed.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal("three", clipboard.ReadText());
        Assert.True(clipboard.Capabilities.Supports(BackendCapability.ClipboardWrite));
    }

    [Fact]
    public void Clipboard_without_display_reports_capability_error()
    {
        var runner = new FakeRunner();
        var options = new X11BackendOptions([], DisplayName: string.Empty, XclipCommand: "xclip-fake");
        using var clipboard = new X11ClipboardService(options, runner, TimeSpan.FromHours(1), hasDisplay: false);

        var error = Assert.Throws<BackendCapabilityException>(() => clipboard.ReadText());
        Assert.Equal(BackendCapability.ClipboardRead, error.Capability);
    }

    private sealed class FakeRunner : IX11CommandRunner
    {
        private readonly object _gate = new();
        public string? Current { get; set; }
        public bool Exists(string command) => command == "xclip-fake";
        public X11CommandResult Run(string command, IReadOnlyList<string> arguments, string? standardInput = null, TimeSpan? timeout = null)
        {
            lock (_gate)
            {
                if (standardInput is not null)
                {
                    Current = standardInput;
                    return new X11CommandResult(0, string.Empty, string.Empty);
                }
                return new X11CommandResult(0, Current ?? string.Empty, string.Empty);
            }
        }
    }
}
