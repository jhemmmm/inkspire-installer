using Inkspire.Panel.App;
using Inkspire.Panel.Processes;

namespace Inkspire.Panel.Services;

/// <summary>Owns the ordered service list, start/stop-all, and the health poll.
/// Start order: PHP pool → nginx → scheduler. Stop order is the reverse.</summary>
public sealed class ServiceManager : IDisposable
{
    private readonly AppServices _ctx;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private Task? _pollLoop;

    public ServiceManager(AppServices ctx)
    {
        _ctx = ctx;
        Php = new PhpPoolService(ctx);
        Nginx = new NginxService(ctx);
        Scheduler = new SchedulerService(ctx);
        All = new ServiceBase[] { Php, Nginx, Scheduler };
        foreach (var s in All) s.StatusChanged += svc => AnyStatusChanged?.Invoke(svc);
    }

    public PhpPoolService Php { get; }
    public NginxService Nginx { get; }
    public SchedulerService Scheduler { get; }
    public IReadOnlyList<ServiceBase> All { get; }
    public IEnumerable<ServiceBase> Enabled => All.Where(s => s.Enabled);

    public event Action<ServiceBase>? AnyStatusChanged;

    public bool AnyRunning => Enabled.Any(s => s.Status.IsActive);

    /// <summary>The web app is answering: nginx serving and the PHP pool up.</summary>
    public bool WebAppServing => Nginx.Status.IsServing && Php.Status.IsServing;

    public ServiceBase? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>Kill nginx/php from a previous panel that died. Matches full paths under the install root.</summary>
    public int SweepOrphans() => ProcessSweep.Sweep(_ctx.Paths.InstallRoot);

    public void StartPolling() => _pollLoop ??= Task.Run(PollLoopAsync);

    private async Task PollLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false)) await PollOnceAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task PollOnceAsync()
    {
        if (!await _pollGate.WaitAsync(0).ConfigureAwait(false)) return; // single-flight
        try
        {
            foreach (var s in Enabled)
            {
                if (!s.Status.IsActive) continue;
                try
                {
                    await s.RefreshAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warn($"{s.Id}: health probe failed: {ex.Message}");
                }
            }
        }
        finally
        {
            _pollGate.Release();
        }
    }

    /// <summary>Starts every enabled service (only the autostart ones when asked). A service that fails to start does not
    /// stop the others: nginx still answers with a clear 502 page while PHP is being fixed.</summary>
    public async Task StartAllAsync(bool onlyAutostart = false, CancellationToken ct = default)
    {
        foreach (var s in All)
        {
            if (!s.Enabled) continue;
            if (onlyAutostart && !s.Autostart) continue;
            await s.StartAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        foreach (var s in All.Reverse())
        {
            if (!s.Enabled) continue;
            await s.StopAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Windows is shutting down / logging off: best effort, no waiting. The job object finishes the rest.</summary>
    public void KillAllNow()
    {
        foreach (var s in All.Reverse())
        {
            try { s.KillNow(); }
            catch { /* shutting down anyway */ }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var s in All) s.Dispose();
        _cts.Dispose();
        _pollGate.Dispose();
    }
}
