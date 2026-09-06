using System.Diagnostics;
using iKeyd.Core.Automation;
using iKeyd.Core.Input;
using iKeyd.Windows.Automation;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class AutomationActionTests
{
    [Fact]
    public void Exec_start_info_preserves_literal_argument_boundaries()
    {
        var request = CommandRequest.Exec("tool.exe", ["hello world", "&danger", "quoted\"value"]);
        var startInfo = WindowsCommandActionQueue.CreateStartInfo(request);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("tool.exe", startInfo.FileName);
        Assert.Equal(["hello world", "&danger", "quoted\"value"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void Shell_start_info_is_an_explicit_comspec_escape_hatch()
    {
        var request = CommandRequest.Shell("echo hello && echo world");
        var startInfo = WindowsCommandActionQueue.CreateStartInfo(request);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", startInfo.FileName);
        Assert.Equal(["/d", "/s", "/c", "echo hello && echo world"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public async Task Execute_async_captures_stdout_stderr_and_exit_code()
    {
        var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var request = CommandRequest.Exec(comspec, ["/d", "/s", "/c", "echo stdout-marker & echo stderr-marker 1>&2 & exit /b 7"]);
        var result = await WindowsCommandActionQueue.ExecuteAsync(request);
        Assert.True(result.Started);
        Assert.False(result.Succeeded);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("stdout-marker", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stderr-marker", result.StandardError, StringComparison.Ordinal);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Full_queue_rejects_without_waiting()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new WindowsCommandActionQueue(
            1,
            async (request, cancellationToken) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return new CommandResult(request, 0, string.Empty, string.Empty, null);
            });

        Assert.True(queue.TryEnqueue(CommandRequest.Exec("first.exe")));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(queue.TryEnqueue(CommandRequest.Exec("second.exe")));
        var before = Stopwatch.GetTimestamp();
        var accepted = queue.TryEnqueue(CommandRequest.Exec("third.exe"));
        var elapsed = Stopwatch.GetElapsedTime(before);
        Assert.False(accepted);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(100));
        release.TrySetResult();
    }

    [Theory]
    [InlineData(SystemQueryKeys.Os)]
    [InlineData(SystemQueryKeys.Architecture)]
    [InlineData(SystemQueryKeys.Hostname)]
    [InlineData(SystemQueryKeys.Username)]
    public void System_query_provider_returns_basic_system_values(string key)
    {
        var provider = new WindowsSystemQueryProvider(new StubInputMethod(false));
        Assert.False(string.IsNullOrWhiteSpace(provider.GetValue(key)));
    }

    [Fact]
    public void System_query_provider_exposes_ime_state_as_stable_scalar()
    {
        var provider = new WindowsSystemQueryProvider(new StubInputMethod(true));
        Assert.Equal("true", provider.GetValue(SystemQueryKeys.ImeKanaActive));
    }

    [Fact]
    public void Unknown_system_query_is_rejected_before_backend_dispatch()
    {
        var provider = new WindowsSystemQueryProvider(new StubInputMethod(false));
        Assert.Throws<ArgumentException>(() => provider.GetValue("system.magic"));
    }

    private sealed class StubInputMethod(bool kanaActive) : IInputMethod
    {
        public bool IsKanaInputActive() => kanaActive;
    }
}
