using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.Runtime;

/// <summary>Applies an edited configuration to a running install: firewall (one UAC prompt), nginx.conf / php.ini / .env, a
/// restart of exactly the services that need it, and the Run-at-login entry. The caller shows progress with <c>log</c>.</summary>
public static class SettingsApplier
{
    public sealed record Result(bool FirewallDeclined, bool FirewallFailed);

    public static bool NeedsServerRestart(PanelConfig before, PanelConfig after) =>
        before.Web.Port != after.Web.Port || before.Web.PoolSize != after.Web.PoolSize || before.Web.PoolBasePort != after.Web.PoolBasePort
        || before.Web.MaxUploadMb != after.Web.MaxUploadMb || before.Web.MaxRequests != after.Web.MaxRequests;

    public static bool NeedsEnvRewrite(PanelConfig before, PanelConfig after) =>
        before.App.Name != after.App.Name || before.App.AppUrl != after.App.AppUrl || before.Web.Port != after.Web.Port;

    public static bool FirewallChanged(PanelConfig before, PanelConfig after) =>
        before.Firewall.Enabled != after.Firewall.Enabled || before.Firewall.Scope != after.Firewall.Scope || before.Web.Port != after.Web.Port;

    public static async Task<Result> ApplyAsync(AppServices ctx, PanelConfig after, Action<string> log, CancellationToken ct)
    {
        var before = ConfigStore.Clone(ctx.Config);
        var declined = false;
        var failed = false;

        if (FirewallChanged(before, after))
        {
            var tasks = new List<AdminTaskRequest> { new("firewall-remove", Array.Empty<string>()) };
            if (after.Firewall.Enabled) tasks.Add(new("firewall-add", new[] { after.Web.Port.ToString(), after.Firewall.Scope.ToString() }));
            log("Updating the Windows Firewall (watch for the administrator prompt)");
            var code = await Elevation.RunAdminTasksAsync(tasks, ctx.Paths.LogsDir, ct).ConfigureAwait(false);
            if (code == Elevation.ExitCancelled)
            {
                declined = true;
                log("Administrator approval was declined: the firewall was NOT changed.");
            }
            else if (code != 0)
            {
                failed = true;
                log($"Windows refused part of the firewall change; see {ctx.Paths.AdminTaskLog}.");
            }
        }

        var restart = NeedsServerRestart(before, after);
        var svc = ctx.Services;
        var wasPhp = svc.Php.Status.IsActive;
        var wasNginx = svc.Nginx.Status.IsActive;
        var wasScheduler = svc.Scheduler.Status.IsActive;

        if (restart)
        {
            log("Stopping the services to apply the new settings");
            await svc.Scheduler.StopAsync(ct).ConfigureAwait(false);
            await svc.Nginx.StopAsync(ct).ConfigureAwait(false);
            await svc.Php.StopAsync(ct).ConfigureAwait(false);
        }

        ctx.ApplyConfig(after);
        ctx.SaveConfig();

        if (restart || NeedsEnvRewrite(before, after))
        {
            var provisioner = new Provisioner(ctx.Paths, after, log);
            provisioner.WriteRuntimeConfigs();
            provisioner.WriteEnv();
        }

        if (restart)
        {
            log("Starting the services again");
            if (wasPhp) await svc.Php.StartAsync(CancellationToken.None).ConfigureAwait(false);
            if (wasNginx) await svc.Nginx.StartAsync(CancellationToken.None).ConfigureAwait(false);
            if (wasScheduler) await svc.Scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        if (NeedsEnvRewrite(before, after) && File.Exists(ctx.Paths.AppArtisan)) await AppRuntime.ReloadAsync(ctx, log, ct).ConfigureAwait(false);

        StartupRegistration.Apply(after.Ui.RunAtLogin, ctx.Paths);
        log("Settings applied.");
        return new Result(declined, failed);
    }
}
