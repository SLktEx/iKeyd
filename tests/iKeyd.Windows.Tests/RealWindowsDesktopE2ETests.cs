using System.Drawing;
using iKeyd.Core.Desktop;
using iKeyd.Windows.Desktop;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class RealWindowsDesktopE2ETests
{
    public const string EnvironmentVariable = "IKEYD_REAL_WINDOWS_E2E";

    [Fact]
    [Trait("Category", "RealWindowsCompatibilityE2E")]
    public void Disposable_window_round_trips_real_Win32_desktop_operations()
    {
        if (!IsEnabled())
            return;

        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "iKeyd #59 disposable verification window",
                StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(120, 120, 640, 420),
                ShowInTaskbar = false
            };
            form.Show();
            form.Activate();
            Pump();

            var backend = new WindowsDesktopBackend();
            var window = new WindowHandle(form.Handle);
            Assert.True(backend.IsWindow(window));
            Assert.True(backend.HasCaption(window));

            backend.MoveResize(window, new DesktopRect(160, 150, 620, 400));
            Assert.True(WaitUntil(() => backend.GetWindowBounds(window) == new DesktopRect(160, 150, 620, 400)));

            backend.SetTopMost(window, true);
            Assert.True(WaitUntil(() => backend.IsTopMost(window)));
            backend.SetTopMost(window, false);
            Assert.True(WaitUntil(() => !backend.IsTopMost(window)));

            backend.SetOpacity(window, 210);
            Assert.True(WaitUntil(() => backend.GetOpacity(window) == 210));
            backend.SetOpacity(window, null);
            Assert.True(WaitUntil(() => backend.GetOpacity(window) is null));

            backend.SetCaption(window, false);
            Assert.True(WaitUntil(() => !backend.HasCaption(window)));
            backend.SetCaption(window, true);
            Assert.True(WaitUntil(() => backend.HasCaption(window)));

            backend.Minimize(window);
            Assert.True(WaitUntil(() => backend.GetWindowState(window) == DesktopWindowState.Minimized));
            backend.Maximize(window);
            Assert.True(WaitUntil(() => backend.GetWindowState(window) == DesktopWindowState.Maximized));
            backend.Restore(window);
            Assert.True(WaitUntil(() => backend.GetWindowState(window) == DesktopWindowState.Normal));

            form.Close();
            Pump();
        });
    }

    [Fact]
    [Trait("Category", "RealWindowsCompatibilityE2E")]
    public void Pointer_absolute_move_uses_real_Win32_and_restores_original_position()
    {
        if (!IsEnabled())
            return;

        var backend = new WindowsDesktopBackend();
        var original = backend.GetPointerPosition();
        var workArea = backend.GetPrimaryWorkArea();
        var target = new DesktopPoint(
            workArea.X + Math.Max(1, workArea.Width / 3),
            workArea.Y + Math.Max(1, workArea.Height / 3));

        try
        {
            backend.MovePointer(target);
            Assert.True(WaitUntil(() => backend.GetPointerPosition() == target));
        }
        finally
        {
            backend.MovePointer(original);
            Assert.True(WaitUntil(() => backend.GetPointerPosition() == original));
        }
    }

    [Fact]
    [Trait("Category", "RealWindowsCompatibilityE2E")]
    public void Pointer_relative_move_uses_SendInput_without_a_large_burst_and_restores_position()
    {
        if (!IsEnabled())
            return;

        var backend = new WindowsDesktopBackend();
        var original = backend.GetPointerPosition();
        var workArea = backend.GetPrimaryWorkArea();
        var anchor = new DesktopPoint(
            workArea.X + Math.Max(1, workArea.Width / 2),
            workArea.Y + Math.Max(1, workArea.Height / 2));

        try
        {
            backend.MovePointer(anchor);
            Assert.True(WaitUntil(() => backend.GetPointerPosition() == anchor));

            // Relative mouse input is intentionally subject to the user's Windows
            // pointer settings, so exact pixel distance is not a compatibility
            // invariant. We verify the real SendInput path moves in the requested
            // direction and does not turn one small step into a runaway burst.
            backend.MovePointerBy(24, 16);
            Assert.True(WaitUntil(() => backend.GetPointerPosition() != anchor));

            var moved = backend.GetPointerPosition();
            var deltaX = moved.X - anchor.X;
            var deltaY = moved.Y - anchor.Y;
            Assert.True(deltaX > 0, $"Relative mouse X moved in the wrong direction: {deltaX}.");
            Assert.True(deltaY > 0, $"Relative mouse Y moved in the wrong direction: {deltaY}.");
            Assert.InRange(deltaX, 1, 512);
            Assert.InRange(deltaY, 1, 512);
        }
        finally
        {
            backend.MovePointer(original);
            Assert.True(WaitUntil(() => backend.GetPointerPosition() == original));
        }
    }

    private static bool IsEnabled()
        => OperatingSystem.IsWindows() &&
           string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal);

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                done.Set();
            }
        })
        {
            IsBackground = true,
            Name = "iKeyd.RealWindowsDesktopE2E"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "Real-Windows desktop E2E timed out.");
        thread.Join(TimeSpan.FromSeconds(2));
        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"Real-Windows desktop E2E failed: {failure}");
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 3000;
        do
        {
            Application.DoEvents();
            if (condition())
                return true;
            Thread.Sleep(25);
        } while (Environment.TickCount64 < deadline);
        return condition();
    }

    private static void Pump()
    {
        Application.DoEvents();
        Thread.Sleep(50);
        Application.DoEvents();
    }
}
