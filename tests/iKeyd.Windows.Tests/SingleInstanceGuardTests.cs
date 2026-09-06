using iKeyd.App;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void DefaultOwnershipScopeIsGlobal()
    {
        Assert.Equal(@"Global\iKeyd.Instance", SingleInstanceGuard.DefaultMutexName);
    }

    [Fact]
    public void NameEasterEggArgumentIsDetectedCaseInsensitively()
    {
        Assert.True(Program.IsNameEasterEggRequested(["--why-the-name"]));
        Assert.True(Program.IsNameEasterEggRequested(["--WHY-THE-NAME"]));
    }

    [Fact]
    public void UnrelatedArgumentsDoNotTriggerNameEasterEgg()
    {
        Assert.False(Program.IsNameEasterEggRequested(["--mode", "K"]));
    }

    [Fact]
    public void SecondConcurrentAcquisitionIsRejected()
    {
        var name = TestMutexName();

        using var first = SingleInstanceGuard.TryAcquire(name);
        using var second = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void OwnershipCanBeAcquiredAgainAfterDispose()
    {
        var name = TestMutexName();

        var first = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(first);
        first.Dispose();

        using var second = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(second);
    }

    [Fact]
    public void SecondaryInvocationDoesNotRunPrimaryStartup()
    {
        var primaryRan = false;
        var secondaryRan = false;

        var started = Program.RunSingleInstance(
            () => null,
            () => primaryRan = true,
            () => secondaryRan = true);

        Assert.False(started);
        Assert.False(primaryRan);
        Assert.True(secondaryRan);
    }

    [Fact]
    public void PrimaryInvocationKeepsLeaseUntilStartupCompletesAndThenDisposesIt()
    {
        var lease = new RecordingDisposable();
        var primaryRanWhileLeaseHeld = false;
        var secondaryRan = false;

        var started = Program.RunSingleInstance(
            () => lease,
            () => primaryRanWhileLeaseHeld = !lease.IsDisposed,
            () => secondaryRan = true);

        Assert.True(started);
        Assert.True(primaryRanWhileLeaseHeld);
        Assert.False(secondaryRan);
        Assert.True(lease.IsDisposed);
    }

    private static string TestMutexName() => $@"Local\iKeyd.Tests.{Guid.NewGuid():N}";

    private sealed class RecordingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
