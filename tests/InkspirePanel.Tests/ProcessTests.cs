using Inkspire.Panel.Processes;
using Xunit;

namespace InkspirePanel.Tests;

public class ProcessTests
{
    private static ManagedProcessOptions Cmd(string script) => new()
    {
        FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
        Arguments = new[] { "/d", "/c", script },
    };

    private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }

    [Fact]
    public async Task Output_is_captured_line_by_line()
    {
        var sink = new OutputBuffer();
        using var p = ManagedProcess.Start(Cmd("echo hello from child"), null, sink);
        var exit = await p.ExitedTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, exit.ExitCode);
        Assert.Contains(sink.Snapshot(), l => l.Contains("hello from child"));
    }

    [Fact]
    public async Task Supervisor_starts_and_stops_a_long_running_process()
    {
        var sink = new OutputBuffer();
        using var sup = new Supervisor("long", () => Cmd("ping -n 60 127.0.0.1 >nul"), null, sink);

        await sup.StartAsync();
        Assert.Equal(SupervisorState.Running, sup.State);
        Assert.NotNull(sup.Pid);

        await sup.StopAsync();
        Assert.Equal(SupervisorState.Stopped, sup.State);
        Assert.Null(sup.Pid);
    }

    [Fact]
    public async Task Supervisor_gives_up_after_the_configured_number_of_consecutive_failures()
    {
        var sink = new OutputBuffer();
        var policy = new RestartPolicy { Delays = new[] { TimeSpan.FromMilliseconds(10) }, MaxConsecutiveFailures = 3 };
        using var sup = new Supervisor("crash", () => Cmd("exit 1"), null, sink, policy);

        await sup.StartAsync();

        Assert.True(await WaitFor(() => sup.State == SupervisorState.Failed), $"state was {sup.State}");
        Assert.Contains(sink.Snapshot(), l => l.Contains("giving up"));
    }

    [Fact]
    public async Task An_expected_exit_is_respawned_and_never_counts_as_a_failure()
    {
        var sink = new OutputBuffer();
        var policy = new RestartPolicy { MaxConsecutiveFailures = 2, IsExpectedExit = e => e.ExitCode == 0 };
        using var sup = new Supervisor("recycle", () => Cmd("exit 0"), null, sink, policy);

        await sup.StartAsync();

        Assert.True(await WaitFor(() => sink.Snapshot().Count(l => l.StartsWith("[start]")) >= 4), "worker was not respawned");
        Assert.NotEqual(SupervisorState.Failed, sup.State);
        await sup.StopAsync();
    }

    [Fact]
    public async Task Stop_cancels_a_pending_restart()
    {
        var sink = new OutputBuffer();
        var policy = new RestartPolicy { Delays = new[] { TimeSpan.FromSeconds(30) }, MaxConsecutiveFailures = 0 };
        using var sup = new Supervisor("backoff", () => Cmd("exit 1"), null, sink, policy);

        await sup.StartAsync();
        Assert.True(await WaitFor(() => sup.State == SupervisorState.WaitingRestart));
        await sup.StopAsync();

        Assert.Equal(SupervisorState.Stopped, sup.State);
        Assert.Null(sup.NextRetryAtUtc);
    }

    [Fact]
    public async Task Closing_the_job_object_kills_the_child()
    {
        var job = new JobObject();
        var sink = new OutputBuffer();
        using var p = ManagedProcess.Start(Cmd("ping -n 60 127.0.0.1 >nul"), job, sink);
        Assert.True(p.IsRunning);

        job.Dispose(); // what happens when the panel crashes

        var finished = await Task.WhenAny(p.ExitedTask, Task.Delay(8000));
        Assert.Same(p.ExitedTask, finished);
    }

    [Fact]
    public void Restart_delays_grow_and_then_repeat_the_last_one()
    {
        var policy = new RestartPolicy { Delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5) } };
        Assert.Equal(TimeSpan.FromSeconds(1), policy.DelayFor(1));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayFor(2));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayFor(40));
    }

    [Fact]
    public void Sweep_ignores_processes_outside_the_install_root()
    {
        // Nothing of ours runs under this made-up root, so nothing may be killed (a dev PC's Laragon nginx stays alive).
        Assert.Equal(0, ProcessSweep.Sweep(@"C:\definitely-not-an-inkspire-root-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void Output_buffer_keeps_only_the_newest_lines()
    {
        var buffer = new OutputBuffer(3);
        foreach (var i in Enumerable.Range(1, 5)) buffer.Append("l" + i);
        Assert.Equal(new[] { "l3", "l4", "l5" }, buffer.Snapshot());
    }
}
