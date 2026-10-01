using Inkspire.Panel.App;

namespace Inkspire.Panel.Processes;

public enum SupervisorState
{
    Stopped,
    Starting,
    Running,
    WaitingRestart,
    Failed,
    Stopping,
}

/// <summary>Keeps one process alive: starts it, restarts it with back-off when it dies, stops it on request.
/// All state changes happen under one lock; <see cref="StateChanged"/> is raised outside it.</summary>
public sealed class Supervisor : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<ManagedProcessOptions> _optionsFactory;
    private readonly JobObject? _job;
    private readonly OutputBuffer _sink;
    private readonly RestartPolicy _policy;
    private CancellationTokenSource? _retryCts;
    private ManagedProcess? _current;
    private int _consecutiveFailures;
    private bool _desired;
    private bool _disposed;

    public Supervisor(string name, Func<ManagedProcessOptions> optionsFactory, JobObject? job, OutputBuffer sink, RestartPolicy? policy = null)
    {
        Name = name;
        _optionsFactory = optionsFactory;
        _job = job;
        _sink = sink;
        _policy = policy ?? new RestartPolicy();
    }

    public string Name { get; }
    public SupervisorState State { get; private set; } = SupervisorState.Stopped;
    public string? LastMessage { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }

    public event Action<SupervisorState>? StateChanged;

    public ManagedProcess? Current
    {
        get { lock (_gate) return _current; }
    }

    public int? Pid
    {
        get { lock (_gate) return _current is { IsRunning: true } c ? c.Pid : null; }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            _desired = true;
            _consecutiveFailures = 0;
            if (State is SupervisorState.Running or SupervisorState.Starting) return Task.CompletedTask;
            _retryCts?.Cancel();
            SpawnLocked();
        }
        Notify();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        ManagedProcess? proc;
        lock (_gate)
        {
            _desired = false;
            _retryCts?.Cancel();
            NextRetryAtUtc = null;
            proc = _current;
            State = proc is null ? SupervisorState.Stopped : SupervisorState.Stopping;
        }
        Notify();
        if (proc is null) return;

        await proc.StopAsync(ct).ConfigureAwait(false);
        lock (_gate)
        {
            if (State == SupervisorState.Stopping && !proc.IsRunning) State = SupervisorState.Stopped;
        }
        Notify();
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await StopAsync(ct).ConfigureAwait(false);
        await StartAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Immediate, non-graceful: Windows is shutting down or the panel is exiting.</summary>
    public void KillNow()
    {
        ManagedProcess? proc;
        lock (_gate)
        {
            _desired = false;
            _retryCts?.Cancel();
            proc = _current;
        }
        proc?.KillNow();
    }

    private void SpawnLocked()
    {
        State = SupervisorState.Starting;
        try
        {
            var proc = ManagedProcess.Start(_optionsFactory(), _job, _sink);
            _current = proc;
            LastMessage = null;
            NextRetryAtUtc = null;
            State = SupervisorState.Running;
            _ = proc.ExitedTask.ContinueWith(t => OnExited(proc, t.Result), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _sink.Append("[supervisor] " + ex.Message);
            Log.Warn($"{Name}: {ex.Message}");
            HandleFailureLocked(ex.Message);
        }
    }

    private void OnExited(ManagedProcess proc, ProcessExit exit)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(proc, _current)) return;
            _current = null;
            proc.Dispose();

            if (!_desired || exit.Requested)
            {
                State = SupervisorState.Stopped;
            }
            else
            {
                _sink.Append($"[exit] code {exit.ExitCode} after {FormatUptime(exit.Uptime)}");
                if (_policy.IsExpectedExit?.Invoke(exit) == true)
                {
                    _consecutiveFailures = 0;
                    SpawnLocked();
                }
                else
                {
                    if (exit.Uptime >= _policy.StableAfter) _consecutiveFailures = 0;
                    HandleFailureLocked(_policy.Describe?.Invoke(exit) ?? $"exited with code {exit.ExitCode}");
                }
            }
        }
        Notify();
    }

    private void HandleFailureLocked(string message)
    {
        _consecutiveFailures++;
        LastMessage = message;
        if (_policy.MaxConsecutiveFailures > 0 && _consecutiveFailures >= _policy.MaxConsecutiveFailures)
        {
            _desired = false;
            NextRetryAtUtc = null;
            State = SupervisorState.Failed;
            _sink.Append($"[supervisor] giving up after {_consecutiveFailures} failed starts: {message}");
            return;
        }

        var delay = _policy.DelayFor(_consecutiveFailures);
        _retryCts?.Cancel();
        var cts = _retryCts = new CancellationTokenSource();
        NextRetryAtUtc = DateTime.UtcNow + delay;
        State = SupervisorState.WaitingRestart;
        _sink.Append($"[supervisor] {message}; retrying in {delay.TotalSeconds:0}s");
        _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            lock (_gate)
            {
                if (!_desired || _disposed || cts.IsCancellationRequested) return;
                SpawnLocked();
            }
            Notify();
        }, TaskScheduler.Default);
    }

    private void Notify()
    {
        SupervisorState state;
        lock (_gate) state = State;
        try { StateChanged?.Invoke(state); }
        catch (Exception ex) { Log.Error($"{Name}: StateChanged handler failed", ex); }
    }

    private static string FormatUptime(TimeSpan t) => t.TotalSeconds < 120 ? $"{t.TotalSeconds:0.#}s" : $"{t.TotalMinutes:0.#}min";

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        KillNow();
        _retryCts?.Dispose();
    }
}
