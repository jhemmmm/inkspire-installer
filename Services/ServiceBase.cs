using Inkspire.Panel.App;
using Inkspire.Panel.Processes;

namespace Inkspire.Panel.Services;

public enum ServiceState
{
    Stopped,
    Starting,
    Running,
    /// <summary>Process is up but the health probe disagrees (nginx answering 502, a php worker not accepting).</summary>
    Degraded,
    WaitingRestart,
    Failed,
    Stopping,
}

public sealed record ServiceStatus(ServiceState State, IReadOnlyList<int> Pids, string? Detail, DateTime? NextRetryUtc, DateTime? StartedAtUtc)
{
    public static readonly ServiceStatus Stopped = new(ServiceState.Stopped, Array.Empty<int>(), null, null, null);

    public static ServiceStatus Of(ServiceState state, string? detail = null) =>
        new(state, Array.Empty<int>(), detail, null, null);

    /// <summary>Starting, running or about to be restarted: the user would expect Stop to be offered.</summary>
    public bool IsActive => State is not (ServiceState.Stopped or ServiceState.Failed);

    public bool IsServing => State is ServiceState.Running or ServiceState.Degraded;

    public bool Equals(ServiceStatus? other) =>
        other is not null && State == other.State && Detail == other.Detail && NextRetryUtc == other.NextRetryUtc
        && StartedAtUtc == other.StartedAtUtc && Pids.SequenceEqual(other.Pids);

    public override int GetHashCode() => HashCode.Combine(State, Detail, NextRetryUtc, StartedAtUtc, Pids.Count);
}

/// <summary>Something the log viewer can show: a live console buffer, or the newest file matching a pattern in a folder.</summary>
public sealed record LogSource(string Name, OutputBuffer? Buffer, string? FileDir, string? FilePattern);

/// <summary>One row of the panel. Start/stop/restart are serialized; failures become a <see cref="ServiceState.Failed"/>
/// status with the reason instead of an exception the UI would have to catch.</summary>
public abstract class ServiceBase : IDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private ServiceStatus _status = ServiceStatus.Stopped;

    protected ServiceBase(string id, string title, AppServices ctx)
    {
        Id = id;
        Title = title;
        Ctx = ctx;
    }

    public string Id { get; }
    public string Title { get; }
    protected AppServices Ctx { get; }

    public OutputBuffer Sink { get; } = new();
    public abstract string Description { get; }
    public virtual string PortLabel => "";
    public abstract IReadOnlyList<LogSource> LogSources { get; }

    public bool Enabled => Ctx.Config.Services.Get(Id).Enabled;
    public bool Autostart => Ctx.Config.Services.Get(Id).Autostart;

    /// <summary>The user (or autostart) asked for it to run. Cleared by Stop and by a start that fails.</summary>
    public bool Desired { get; private set; }

    public ServiceStatus Status
    {
        get => _status;
        protected set
        {
            if (_status.Equals(value)) return;
            _status = value;
            StatusChanged?.Invoke(this);
        }
    }

    public event Action<ServiceBase>? StatusChanged;

    /// <summary>Returns false (and sets a Failed status) when the service could not be started.</summary>
    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Desired = true;
            await DoStartAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Desired = false;
            Sink.Append("[error] " + ex.Message);
            Log.Warn($"{Id}: start failed: {ex.Message}");
            Status = new ServiceStatus(ServiceState.Failed, Array.Empty<int>(), ex.Message, null, null);
            return false;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Desired = false;
            await DoStopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"{Id}: stop failed: {ex.Message}");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<bool> RestartAsync(CancellationToken ct = default)
    {
        await StopAsync(ct).ConfigureAwait(false);
        return await StartAsync(ct).ConfigureAwait(false);
    }

    protected abstract Task DoStartAsync(CancellationToken ct);
    protected abstract Task DoStopAsync(CancellationToken ct);

    /// <summary>Health probe, called by the manager every couple of seconds while the service is active.</summary>
    public virtual Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Windows is shutting down: kill, don't wait.</summary>
    public virtual void KillNow()
    {
    }

    protected static void RequireFile(string path, string what)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{what} not found at {path}. Run setup (Re-run setup) to repair the install.");
    }

    public virtual void Dispose() => _lifecycle.Dispose();
}
