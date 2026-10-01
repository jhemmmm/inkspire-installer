using System.Diagnostics;
using Inkspire.Panel.App;
using Inkspire.Panel.Network;
using Inkspire.Panel.Processes;

namespace Inkspire.Panel.Services;

/// <summary>nginx: the front door on 0.0.0.0:&lt;port&gt;. Stopped gracefully with <c>nginx -s quit</c>.</summary>
public sealed class NginxService : SupervisedService
{
    public NginxService(AppServices ctx) : base("nginx", "Web server (nginx)", ctx)
    {
    }

    public override string PortLabel => $":{Ctx.Config.Web.Port}";
    public override string Description => "Serves Inkspire to browsers on this PC and on the shop network.";

    public override IReadOnlyList<LogSource> LogSources => new[]
    {
        new LogSource("nginx (console)", Sink, null, null),
        new LogSource("nginx error.log", null, Ctx.Paths.NginxLogsDir, "error.log"),
        new LogSource("nginx access.log", null, Ctx.Paths.NginxLogsDir, "access.log"),
        new LogSource("Inkspire (laravel.log)", null, Ctx.Paths.LaravelLogsDir, "laravel*.log"),
    };

    protected override TimeSpan StartupGrace => TimeSpan.FromSeconds(20);

    // /up boots Laravel, so a healthy server is probed gently.
    protected override TimeSpan ProbeInterval => TimeSpan.FromSeconds(10);

    protected override RestartPolicy Policy => new()
    {
        Delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) },
        MaxConsecutiveFailures = 5,
        Describe = exit => exit.Uptime < TimeSpan.FromSeconds(3)
            ? "nginx exited immediately; see nginx error.log (port in use or bad config?)"
            : null,
    };

    protected override IHealthProbe Probe => new HttpProbe(Ctx.Http, $"http://127.0.0.1:{Ctx.Config.Web.Port}/up", status => status switch
    {
        >= 200 and < 300 => HealthResult.Healthy,
        502 or 504 => HealthResult.Degraded("PHP workers are not answering (HTTP 502)"),
        503 => HealthResult.Degraded("Inkspire is in maintenance mode (HTTP 503)"),
        404 => HealthResult.Degraded("The app does not answer /up (HTTP 404): is it installed?"),
        _ => HealthResult.Degraded($"HTTP {status}"),
    });

    private string Prefix => AppPaths.Fwd(Ctx.Paths.NginxDir) + "/";

    protected override ManagedProcessOptions BuildOptions() => new()
    {
        FileName = Ctx.Paths.NginxExe,
        Arguments = new[] { "-p", Prefix, "-c", "conf/nginx.conf" },
        WorkingDirectory = Ctx.Paths.NginxDir,
        GracefulStop = GracefulStopMode.Custom,
        GracefulTimeout = TimeSpan.FromSeconds(5),
        CustomStop = (_, ct) => RunNginxAsync(new[] { "-s", "quit" }, ct),
    };

    protected override async Task PreflightAsync(CancellationToken ct)
    {
        RequireFile(Ctx.Paths.NginxExe, "nginx.exe");
        RequireFile(Ctx.Paths.NginxConf, "nginx.conf");
        Directory.CreateDirectory(Ctx.Paths.NginxLogsDir);
        Directory.CreateDirectory(Ctx.Paths.NginxTempDir);

        var port = Ctx.Config.Web.Port;
        if (!PortProbe.IsFree(port))
        {
            var owner = PortProbe.WhoHolds(port);
            var who = owner is null ? "another program" : $"{owner.ProcessName} (pid {owner.Pid})";
            throw new InvalidOperationException($"Port {port} is already in use by {who}. {owner?.Hint}".Trim());
        }

        var (code, output) = await RunNginxAsync(new[] { "-t" }, ct).ConfigureAwait(false);
        if (code != 0) throw new InvalidOperationException("nginx configuration test failed: " + output.Trim());
    }

    private async Task<(int Code, string Output)> RunNginxAsync(IEnumerable<string> extraArgs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Ctx.Paths.NginxExe)
        {
            WorkingDirectory = Ctx.Paths.NginxDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(Prefix);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("conf/nginx.conf");
        foreach (var a in extraArgs) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("nginx did not start");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Sink.Append(line.TrimEnd('\r'));
        return (p.ExitCode, output);
    }

    /// <summary>Ask a running nginx to reopen its log files (after the panel rotated them).</summary>
    public Task ReopenLogsAsync(CancellationToken ct = default) => RunNginxAsync(new[] { "-s", "reopen" }, ct);
}
