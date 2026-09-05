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

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{command}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
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

        Task.WaitAll(stdoutTask, stderrTask);
        return new WaylandCommandResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
