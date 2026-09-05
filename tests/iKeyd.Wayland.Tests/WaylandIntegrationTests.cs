using iKeyd.Core.Desktop;
using iKeyd.Core.Platform;
using iKeyd.Wayland.Clipboard;
using iKeyd.Wayland.Input;
using Xunit;

namespace iKeyd.Wayland.Tests;

public sealed class WaylandIntegrationTests
{
    [Fact]
    [Trait("Category", "WaylandIntegration")]
    public void Clipboard_round_trips_in_a_real_wayland_session()
    {
        if (Environment.GetEnvironmentVariable("IKEYD_WAYLAND_INTEGRATION") != "1")
            return;
        Assert.True(OperatingSystem.IsLinux());

        var options = WaylandBackendOptions.Detect() with { KeyboardDevicePaths = [] };
        using var clipboard = new WaylandClipboardService(options);
        Assert.True(clipboard.Capabilities.Supports(BackendCapability.ClipboardRead));
        Assert.True(clipboard.Capabilities.Supports(BackendCapability.ClipboardWrite));

        var expected = $"iKeyd-wayland-{Guid.NewGuid():N}";
        clipboard.WriteText(expected);

        string? actual = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            actual = clipboard.ReadText();
            if (actual == expected)
                break;
            Thread.Sleep(100);
        }
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Category", "WaylandIntegration")]
    public void Uinput_device_can_emit_keyboard_pointer_and_media_events_when_available()
    {
        if (Environment.GetEnvironmentVariable("IKEYD_UINPUT_INTEGRATION") != "1")
            return;
        Assert.True(OperatingSystem.IsLinux());

        var path = Environment.GetEnvironmentVariable("IKEYD_UINPUT") ?? "/dev/uinput";
        using var device = new LinuxUInputDevice(path);

        device.SendText("a1");
        device.MovePointerBy(3, -2);
        device.ClickMouseButton(0x110);
        device.ScrollVertical(1);
        device.SendMediaKey(164);

        Assert.True(device.Capabilities.Supports(BackendCapability.KeyboardOutput));
        Assert.True(device.Capabilities.Supports(BackendCapability.PointerRelative));
        Assert.True(device.Capabilities.Supports(BackendCapability.MediaKeys));
    }
}
