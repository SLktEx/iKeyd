using System.Diagnostics;
using iKeyd.Core.Desktop;
using iKeyd.Core.Platform;
using iKeyd.X11.Clipboard;
using iKeyd.X11.Desktop;
using iKeyd.X11.Interop;
using Xunit;

namespace iKeyd.X11.Tests;

public sealed class X11IntegrationTests
{
    [Fact]
    [Trait("Category", "X11Integration")]
    public void Clipboard_round_trips_on_real_X11_selection()
    {
        if (!Enabled()) return;
        using var clipboard = new X11ClipboardService();
        Assert.True(clipboard.Capabilities.Supports(BackendCapability.ClipboardRead));
        Assert.True(clipboard.Capabilities.Supports(BackendCapability.ClipboardWrite));

        var expected = $"iKeyd-x11-{Guid.NewGuid():N}";
        clipboard.WriteText(expected);
        string? actual = null;
        for (var i = 0; i < 30; i++)
        {
            actual = clipboard.ReadText();
            if (actual == expected) break;
            Thread.Sleep(100);
        }
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Category", "X11Integration")]
    public void Ewmh_window_and_XTest_pointer_operations_work_with_a_window_manager()
    {
        if (!Enabled()) return;
        using var connection = new X11Connection();
        Assert.True(connection.HasXTest);
        using var desktop = new X11DesktopBackend(connection);
        Assert.True(desktop.Capabilities.Supports(BackendCapability.WindowMoveResize));
        Assert.True(desktop.Capabilities.Supports(BackendCapability.PointerAbsolute));

        var originalPointer = desktop.GetPointerPosition();
        desktop.MovePointer(new DesktopPoint(120, 130));
        Assert.True(WaitUntil(() =>
        {
            var p = desktop.GetPointerPosition();
            return Math.Abs(p.X - 120) <= 2 && Math.Abs(p.Y - 130) <= 2;
        }));

        using var xterm = StartXterm();
        var window = WaitForXterm(desktop);
        Assert.False(window.IsEmpty);
        Assert.Contains("XTerm", desktop.GetWindowClass(window) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(window, desktop.EnumerateTopLevelWindows());

        desktop.Activate(window);
        Assert.True(WaitUntil(() => desktop.GetActiveWindow() == window));

        var workArea = desktop.GetPrimaryWorkArea();
        Assert.True(workArea.Width > 0 && workArea.Height > 0);

        desktop.MoveResize(window, new DesktopRect(80, 90, 520, 320));
        Assert.True(WaitUntil(() =>
        {
            var bounds = desktop.GetWindowBounds(window);
            return bounds.Width >= 500 && bounds.Height >= 280;
        }));

        desktop.SetTopMost(window, true);
        Assert.True(WaitUntil(() => desktop.IsTopMost(window)));
        desktop.SetTopMost(window, false);
        Assert.True(WaitUntil(() => !desktop.IsTopMost(window)));

        desktop.SetOpacity(window, 128);
        Assert.True(WaitUntil(() => desktop.GetOpacity(window) is >= 126 and <= 130));
        desktop.SetOpacity(window, null);
        Assert.Null(desktop.GetOpacity(window));

        desktop.Maximize(window);
        Assert.True(WaitUntil(() => desktop.GetWindowState(window) == DesktopWindowState.Maximized));
        desktop.Restore(window);
        Assert.True(WaitUntil(() => desktop.GetWindowState(window) != DesktopWindowState.Maximized));

        desktop.MovePointer(originalPointer);
    }

    private static bool Enabled()
        => Environment.GetEnvironmentVariable("IKEYD_X11_INTEGRATION") == "1" && OperatingSystem.IsLinux();

    private static Process StartXterm()
    {
        var info = new ProcessStartInfo
        {
            FileName = "xterm",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-T");
        info.ArgumentList.Add("iKeyd X11 Integration");
        info.ArgumentList.Add("-geometry");
        info.ArgumentList.Add("40x10+40+40");
        info.ArgumentList.Add("-e");
        info.ArgumentList.Add("sh");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("sleep 30");
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start xterm.");
    }

    private static WindowHandle WaitForXterm(X11DesktopBackend desktop)
    {
        WindowHandle result = default;
        Assert.True(WaitUntil(() =>
        {
            foreach (var window in desktop.EnumerateTopLevelWindows())
            {
                if ((desktop.GetWindowClass(window) ?? string.Empty).Contains("XTerm", StringComparison.OrdinalIgnoreCase))
                {
                    result = window;
                    return true;
                }
            }
            return false;
        }));
        return result;
    }

    private static bool WaitUntil(Func<bool> predicate, int attempts = 60, int delayMs = 100)
    {
        for (var i = 0; i < attempts; i++)
        {
            try { if (predicate()) return true; } catch { }
            Thread.Sleep(delayMs);
        }
        return false;
    }
}
