namespace iKeyd.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    internal const string DefaultMutexName = @"Global\iKeyd.Instance";

    private Mutex? _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    internal static SingleInstanceGuard? TryAcquire(string mutexName = DefaultMutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
            throw new ArgumentException("A mutex name is required.", nameof(mutexName));

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceGuard(mutex);
        }
        catch
        {
            mutex?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
            return;

        try
        {
            if (_ownsMutex)
            {
                mutex.ReleaseMutex();
                _ownsMutex = false;
            }
        }
        catch (ApplicationException)
        {
            // The owning thread/process is already unwinding. Disposing the handle is enough.
        }
        finally
        {
            mutex.Dispose();
        }
    }
}
