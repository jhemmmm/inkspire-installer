using Inkspire.Panel.App;
using Inkspire.Panel.Processes;

namespace Inkspire.Panel.Services;

/// <summary>A service that is exactly one supervised process, with an optional health probe.</summary>
public abstract class SupervisedService : ServiceBase
{
    private Supervisor? _supervisor;
    private HealthResult _health = HealthResult.Healthy;
    private DateTime _lastProbeUtc = DateTime.MinValue;

    protected SupervisedService(string id, string title, AppServices ctx) : base(id, title, ctx)
    {
    }

    protected abstract ManagedProcessOptions BuildOptions();
    protected virtual RestartPolicy Policy => new();
    protected virtual IHealthProbe? Probe => null;

    /// <summary>After a start, probe failures are reported as "Starting" rather than "Degraded" for this long.</summary>
    protected virtual TimeSpan StartupGrace => TimeSpan.FromSeconds(10);

    /// <summary>How often a healthy service is probed. Slow probes (an HTTP request into PHP) override this.</summary>
    protected virtual TimeSpan ProbeInterval => TimeSpan.FromSeconds(2);

    protected virtual Task PreflightAsync(CancellationToken ct) => Task.CompletedTask;

    protected override async Task DoStartAsync(CancellationToken ct)
    {
        Status = ServiceStatus.Of(ServiceState.Starting);
        await PreflightAsync(ct).ConfigureAwait(false);
        _health = HealthResult.Down("starting");
        _lastProbeUtc = DateTime.MinValue;
        if (_supervisor is null)
        {
            _supervisor = new Supervisor(Id, BuildOptions, Ctx.Job, Sink, Policy);
            _supervisor.StateChanged += _ => Recompute();
        }
        await _supervisor.StartAsync(ct).ConfigureAwait(false);
        Recompute();
    }

    protected override async Task DoStopAsync(CancellationToken ct)
    {
        if (_supervisor is null)
        {
            Status = ServiceStatus.Stopped;
            return;
        }
        await _supervisor.StopAsync(ct).ConfigureAwait(false);
        Recompute();
    }

    public override async Task RefreshAsync(CancellationToken ct)
    {
        var sup = _supervisor;
        if (sup is null || sup.State != SupervisorState.Running || Probe is not { } probe) return;

        var settled = _health.Level == HealthLevel.Ok;
        if (settled && DateTime.UtcNow - _lastProbeUtc < ProbeInterval) return;
        _lastProbeUtc = DateTime.UtcNow;
        _health = await probe.ProbeAsync(ct).ConfigureAwait(false);
        Recompute();
    }

    public override void KillNow() => _supervisor?.KillNow();

    protected void Recompute()
    {
        var sup = _supervisor;
        if (sup is null)
        {
            Status = ServiceStatus.Stopped;
            return;
        }
        var pids = sup.Pid is { } pid ? new[] { pid } : Array.Empty<int>();
        var started = sup.Current?.StartedAtUtc;
        Status = sup.State switch
        {
            SupervisorState.Stopped => ServiceStatus.Stopped,
            SupervisorState.Starting => new ServiceStatus(ServiceState.Starting, pids, null, null, started),
            SupervisorState.Running => FromHealth(pids, started),
            SupervisorState.WaitingRestart => new ServiceStatus(ServiceState.WaitingRestart, pids, sup.LastMessage, sup.NextRetryAtUtc, null),
            SupervisorState.Failed => new ServiceStatus(ServiceState.Failed, pids, sup.LastMessage, null, null),
            _ => new ServiceStatus(ServiceState.Stopping, pids, null, null, started),
        };
    }

    private ServiceStatus FromHealth(IReadOnlyList<int> pids, DateTime? started)
    {
        if (Probe is null || _health.Level == HealthLevel.Ok)
            return new ServiceStatus(ServiceState.Running, pids, null, null, started);

        var inGrace = started is { } s && DateTime.UtcNow - s < StartupGrace;
        return inGrace
            ? new ServiceStatus(ServiceState.Starting, pids, null, null, started)
            : new ServiceStatus(ServiceState.Degraded, pids, _health.Message, null, started);
    }

    public override void Dispose()
    {
        _supervisor?.Dispose();
        base.Dispose();
    }
}
