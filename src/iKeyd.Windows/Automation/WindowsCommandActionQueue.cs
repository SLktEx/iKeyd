using System.Diagnostics;
using System.Threading.Channels;
using iKeyd.Core.Automation;

namespace iKeyd.Windows.Automation;

public sealed class WindowsCommandActionQueue : ICommandActionQueue
{
    public const int DefaultCapacity = 64;

    private readonly Channel<CommandRequest> _channel;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<CommandRequest, CancellationToken, Task<CommandResult>> _execute;
    private readonly Task _worker;
    private CommandResult? _lastResult;
    private bool _disposed;

    public WindowsCommandActionQueue(int capacity = DefaultCapacity)
        : this(capacity, ExecuteAsync)
    {
    }

    internal WindowsCommandActionQueue(
        int capacity,
        Func<CommandRequest, CancellationToken, Task<CommandResult>> execute)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Command queue capacity must be positive.");

        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _channel = Channel.CreateBounded<CommandRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(RunWorkerAsync);
    }

    public CommandResult? LastResult => Volatile.Read(ref _lastResult);

    public event Action<CommandResult>? Completed;

    public bool TryEnqueue(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed)
            return false;

        return _channel.Writer.TryWrite(request);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _channel.Writer.TryComplete();
        _cancellation.Cancel();
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                var result = await _execute(request, _cancellation.Token).ConfigureAwait(false);
                Volatile.Write(ref _lastResult, result);
                PublishCompleted(result);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    private void PublishCompleted(CommandResult result)
    {
        var handlers = Completed;
        if (handlers is null)
            return;

        foreach (Action<CommandResult> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(result);
            }
            catch
            {
            }
        }
    }

    internal static async Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var process = new Process
            {
                StartInfo = CreateStartInfo(request),
                EnableRaisingEvents = false
            };

            if (!process.Start())
                return new CommandResult(request, null, string.Empty, string.Empty, "Process.Start returned false.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new CommandResult(request, process.ExitCode, stdout, stderr, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CommandResult(request, null, string.Empty, string.Empty, exception.Message);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (request.Kind == CommandRequestKind.Exec)
        {
            startInfo.FileName = request.Command;
            foreach (var argument in request.Arguments)
                startInfo.ArgumentList.Add(argument);
            return startInfo;
        }

        startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(request.Command);
        return startInfo;
    }
}
