using System.Diagnostics;

namespace iKeyd.X11.Clipboard;

public sealed record X11CommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IX11CommandRunner
{
    bool Exists(string command);
    X11CommandResult Run(string command, IReadOnlyList<string> arguments, string? standardInput = null, TimeSpan? timeout = null);
}

public sealed class SystemX11CommandRunner : IX11CommandRunner
{
    public bool Exists(string command) => X11BackendProbe.CommandExists(command);

    public X11CommandResult Run(string command, IReadOnlyList<string> arguments, string? standardInput = null, TimeSpan? timeout = null)
    {
        var capture = standardInput is null;
        var info = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start '{command}'.");
        Task<string>? stdout = capture ? process.StandardOutput.ReadToEndAsync() : null;
        Task<string>? stderr = capture ? process.StandardError.ReadToEndAsync() : null;
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
        if (!capture) return new X11CommandResult(process.ExitCode, string.Empty, string.Empty);
        Task.WaitAll(stdout!, stderr!);
        return new X11CommandResult(process.ExitCode, stdout!.Result, stderr!.Result);
    }
}
