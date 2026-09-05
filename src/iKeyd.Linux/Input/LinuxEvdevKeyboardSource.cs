using iKeyd.Core.Input;

namespace iKeyd.Linux.Input;

public sealed class LinuxEvdevKeyboardSource : IKeyboardInputSource, IDisposable
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<string> _devicePaths;
    private readonly ILinuxVirtualInput _passThroughOutput;
    private readonly LinuxEvdevKeyMap _keyMap;
    private readonly bool _grab;
    private readonly List<DeviceReader> _readers = [];
    private CancellationTokenSource? _cancellation;
    private IKeyboardEventHandler? _handler;
    private bool _disposed;

    public LinuxEvdevKeyboardSource(
        IReadOnlyList<string> devicePaths,
        ILinuxVirtualInput passThroughOutput,
        LinuxEvdevKeyMap? keyMap = null,
        bool grab = true)
    {
        _devicePaths = devicePaths ?? throw new ArgumentNullException(nameof(devicePaths));
        _passThroughOutput = passThroughOutput ?? throw new ArgumentNullException(nameof(passThroughOutput));
        _keyMap = keyMap ?? new LinuxEvdevKeyMap();
        _grab = grab;
    }

    public bool IsRunning { get { lock (_gate) return _cancellation is not null; } }
    public Exception? LastError { get; private set; }

    public void Start(IKeyboardEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancellation is not null) throw new InvalidOperationException("evdev keyboard source is already running.");
            if (_devicePaths.Count == 0) throw new InvalidOperationException("No evdev keyboard devices were configured.");

            _handler = handler;
            LastError = null;
            _cancellation = new CancellationTokenSource();
            try
            {
                foreach (var path in _devicePaths)
                {
                    var fd = LinuxNative.Open(path, LinuxNative.OReadOnly | LinuxNative.ONonBlock);
                    try
                    {
                        if (_grab) LinuxNative.IoctlInt(fd, LinuxNative.EviocGrab, 1, $"EVIOCGRAB('{path}')");
                        var reader = new DeviceReader(path, fd);
                        reader.Thread = new Thread(() => ReadLoop(reader, _cancellation.Token))
                        {
                            IsBackground = true,
                            Name = $"iKeyd evdev {Path.GetFileName(path)}"
                        };
                        _readers.Add(reader);
                    }
                    catch
                    {
                        LinuxNative.Close(fd);
                        throw;
                    }
                }
                foreach (var reader in _readers) reader.Thread!.Start();
            }
            catch
            {
                CleanupReaders();
                _cancellation.Dispose();
                _cancellation = null;
                _handler = null;
                throw;
            }
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        DeviceReader[] readers;
        lock (_gate)
        {
            cancellation = _cancellation;
            if (cancellation is null) return;
            cancellation.Cancel();
            readers = _readers.ToArray();
        }

        foreach (var reader in readers)
            if (reader.Thread is { } thread && !ReferenceEquals(thread, Thread.CurrentThread)) thread.Join();

        lock (_gate)
        {
            CleanupReaders();
            _handler = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        GC.SuppressFinalize(this);
    }

    private void ReadLoop(DeviceReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!LinuxNative.TryReadInputEvent(reader.Fd, out var inputEvent, out var error))
                {
                    if (error is LinuxNative.EAgain or LinuxNative.EIntr) { Thread.Sleep(2); continue; }
                    if (error == 0 || error == LinuxNative.EBadF || cancellationToken.IsCancellationRequested) break;
                    throw LinuxNative.Error($"read('{reader.Path}') failed");
                }
                if (inputEvent.Type != LinuxInputCodes.EvKey || inputEvent.Value is < 0 or > 2) continue;
                HandleKeyEvent(inputEvent);
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            LastError = error;
        }
    }

    private void HandleKeyEvent(LinuxInputEvent inputEvent)
    {
        var handler = _handler;
        if (handler is null) return;

        var disposition = KeyboardDisposition.PassThrough;
        if (_keyMap.TryFromEvdev(inputEvent.Code, out var key))
        {
            try
            {
                disposition = handler.OnKeyboardEvent(new KeyboardEvent(
                    key,
                    inputEvent.Value == 0 ? KeyEventKind.Up : KeyEventKind.Down,
                    KeyEventOrigin.Physical,
                    Environment.TickCount64));
            }
            catch
            {
                disposition = KeyboardDisposition.PassThrough;
            }
        }

        if (_grab && disposition == KeyboardDisposition.PassThrough)
            _passThroughOutput.EmitKeyCode(inputEvent.Code, inputEvent.Value);
    }

    private void CleanupReaders()
    {
        foreach (var reader in _readers)
        {
            if (_grab)
            {
                try { LinuxNative.IoctlInt(reader.Fd, LinuxNative.EviocGrab, 0, $"EVIOCGRAB release('{reader.Path}')"); } catch { }
            }
            LinuxNative.Close(reader.Fd);
        }
        _readers.Clear();
    }

    private sealed class DeviceReader(string path, int fd)
    {
        public string Path { get; } = path;
        public int Fd { get; } = fd;
        public Thread? Thread { get; set; }
    }
}
