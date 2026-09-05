using System.Diagnostics;

namespace iKeyd.Wayland.Clipboard;

public sealed record WaylandCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IWaylandCommandRunner
{
    bool Exists(string command);
    WaylandCommandResult Run(string command, IReadOnlyList<string> arguments, string? standardInput = null, TimeSpan? timeout = null);
}

public sealed class SystemWaylandCommandRunner : IWaylandCommandRunner
{
    public bool Exists(string command) => CommandSearch.Exists(command);

    public WaylandCommandResult Run(
        string command,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);

        // wl-copy normally forks a background selection owner after consuming stdin.
        // If stdout/stderr are redirected, that child inherits the pipe handles and
        // ReadToEndAsync never sees EOF even though the parent process has exited.
        // Input-only commands therefore inherit the caller's stdout/stderr; commands
        // without stdin (notably wl-paste) keep captured output for normal parsing.
        var captureOutput = standardInput is null;
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{command}'.");

        Task<string>? stdoutTask = null;
        Task<string>? stderrTask = null;
        if (captureOutput)
        {
            stdoutTask = process.StandardOutput.ReadToEndAsync();
            stderrTask = process.StandardError.ReadToEndAsync();
        }

        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (!process.WaitForExit((int)Math.Min(int.MaxValue, effectiveTimeout.TotalMilliseconds)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"'{command}' did not exit within {effectiveTimeout}.");
        }

        if (!captureOutput)
            return new WaylandCommandResult(process.ExitCode, string.Empty, string.Empty);

        Task.WaitAll(stdoutTask!, stderrTask!);
        return new WaylandCommandResult(process.ExitCode, stdoutTask!.Result, stderrTask!.Result);
    }
}
