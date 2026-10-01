using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Inkspire.Panel.App;

namespace Inkspire.Panel.Processes;

public enum GracefulStopMode
{
    /// <summary>Kill the process tree straight away.</summary>
    None,
    /// <summary>Run <see cref="ManagedProcessOptions.CustomStop"/> (e.g. <c>nginx -s quit</c>), then kill after the timeout.</summary>
    Custom,
}

public sealed class ManagedProcessOptions
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string? WorkingDirectory { get; init; }
    /// <summary>Extra environment variables. <c>PATH</c> is prepended to, not replaced.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public GracefulStopMode GracefulStop { get; init; } = GracefulStopMode.None;
    public Func<ManagedProcess, CancellationToken, Task>? CustomStop { get; init; }
    public TimeSpan GracefulTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>How a process ended. <see cref="Requested"/> is true when the panel asked it to stop.</summary>
public sealed record ProcessExit(int ExitCode, TimeSpan Uptime, bool Requested);

/// <summary>One child process: hidden window, stdout/stderr captured, member of the panel's job object.</summary>
public sealed class ManagedProcess : IDisposable
{
    private readonly Process _process;
    private readonly ManagedProcessOptions _options;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private readonly TaskCompletionSource<ProcessExit> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _stopRequested;

    private ManagedProcess(Process process, ManagedProcessOptions options)
    {
        _process = process;
        _options = options;
    }

    public int Pid { get; private set; }
    public DateTime StartedAtUtc => _startedAtUtc;
    public Task<ProcessExit> ExitedTask => _exited.Task;
    public bool IsRunning => !_exited.Task.IsCompleted;
    public TimeSpan Uptime => DateTime.UtcNow - _startedAtUtc;

    public static ManagedProcess Start(ManagedProcessOptions options, JobObject? job, OutputBuffer sink)
    {
        var psi = new ProcessStartInfo(options.FileName)
        {
            WorkingDirectory = options.WorkingDirectory ?? Path.GetDirectoryName(options.FileName) ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in options.Arguments) psi.ArgumentList.Add(a);
        foreach (var (key, value) in options.Environment)
        {
            if (key.Equals("PATH", StringComparison.OrdinalIgnoreCase))
            {
                var current = psi.Environment.TryGetValue("PATH", out var p) ? p : "";
                psi.Environment["PATH"] = value + ";" + current;
            }
            else
            {
                psi.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var managed = new ManagedProcess(process, options);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) sink.Append(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) sink.Append(e.Data); };
        process.Exited += (_, _) => managed.OnExited();

        sink.Append($"[start] {Path.GetFileName(options.FileName)} {string.Join(' ', options.Arguments.Select(Quote))}");
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException($"Could not start {options.FileName}: {ex.Message}", ex);
        }
        managed.Pid = process.Id;

        if (job is not null && !job.TryAssign(process))
        {
            Log.Warn($"Could not add {Path.GetFileName(options.FileName)} (pid {managed.Pid}) to the job object; it will not be auto-killed if the panel dies.");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return managed;
    }

    private void OnExited()
    {
        int code;
        try
        {
            _process.WaitForExit(); // flush the redirected streams
            code = _process.ExitCode;
        }
        catch
        {
            code = -1;
        }
        _exited.TrySetResult(new ProcessExit(code, DateTime.UtcNow - _startedAtUtc, Volatile.Read(ref _stopRequested) == 1));
    }

    /// <summary>Graceful stop when configured, then kill the whole process tree.</summary>
    public async Task StopAsync(CancellationToken ct)
    {
        if (!IsRunning) return;
        Interlocked.Exchange(ref _stopRequested, 1);

        if (_options.GracefulStop == GracefulStopMode.Custom && _options.CustomStop is { } stop)
        {
            try
            {
                await stop(this, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn($"Graceful stop of {Path.GetFileName(_options.FileName)} failed: {ex.Message}");
            }
            if (await WaitExitAsync(_options.GracefulTimeout, ct).ConfigureAwait(false)) return;
        }

        Kill();
        await WaitExitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
    }

    public void KillNow()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        Kill();
    }

    private void Kill()
    {
        try { _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { } // already gone
        catch (Win32Exception) { }
    }

    private async Task<bool> WaitExitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var done = await Task.WhenAny(_exited.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);
        return ReferenceEquals(done, _exited.Task);
    }

    private static string Quote(string a) => a.Contains(' ') ? "\"" + a + "\"" : a;

    public void Dispose() => _process.Dispose();
}
