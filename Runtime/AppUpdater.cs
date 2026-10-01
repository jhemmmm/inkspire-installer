using Inkspire.Panel.App;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.Runtime;

/// <summary>Replaces <c>app\</c> with a newer release bundle. Uploads, logs and the database live in <c>data\</c> (linked into
/// the app folder), so only code is swapped. The pre-update snapshot of the database is taken with the services stopped, and
/// when ANYTHING after the swap fails (migration, PHP, caches) the previous code AND the snapshot are put back.</summary>
public sealed class AppUpdater
{
    private readonly AppServices _ctx;

    public AppUpdater(AppServices ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Update from a local bundle zip (downloaded and verified by the caller). Stops and restarts the web services itself.</summary>
    public async Task ApplyAsync(string bundleZip, string newVersion, Action<string> log, CancellationToken ct)
    {
        var paths = _ctx.Paths;
        var svc = _ctx.Services;
        var wasPhp = svc.Php.Status.IsActive;
        var wasNginx = svc.Nginx.Status.IsActive;
        var wasScheduler = svc.Scheduler.Status.IsActive;

        // Unpack while the shop keeps working: it is the slow part, and a bad zip is found before anything is stopped.
        log("Unpacking the new version");
        FolderOps.DeleteTree(paths.AppNewDir);
        ZipExtractor.Extract(bundleZip, paths.AppNewDir, stripTopLevel: true, null, ct);
        if (!File.Exists(Path.Combine(paths.AppNewDir, "artisan")))
        {
            FolderOps.DeleteTree(paths.AppNewDir);
            throw new InvalidDataException("That zip is not an Inkspire app bundle (artisan is missing). Nothing was changed.");
        }

        log("Stopping Inkspire for a moment");
        await svc.Scheduler.StopAsync(ct).ConfigureAwait(false);
        await svc.Nginx.StopAsync(ct).ConfigureAwait(false);
        await svc.Php.StopAsync(ct).ConfigureAwait(false);
        svc.SweepOrphans(); // a worker that has not fully exited still holds a file open in app\

        string? snapshot = null;
        try
        {
            if (File.Exists(paths.SqliteDb))
            {
                log("Backing up the database (kept until you delete it)");
                snapshot = await new SqliteBackup(paths, _ctx.Config).RunAsync(ct, log, SqliteBackup.PreUpdatePrefix).ConfigureAwait(false);
            }

            log("Carrying over the settings (.env)");
            if (File.Exists(paths.AppEnvFile)) File.Copy(paths.AppEnvFile, Path.Combine(paths.AppNewDir, ".env"), overwrite: true);

            log("Switching to the new version");
            FolderOps.SwapFolders(paths.AppDir, paths.AppNewDir, paths.AppOldDir, "app");

            try
            {
                await ProvisionNewVersionAsync(log, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log("The update failed: " + ex.Message);
                log("Putting the previous version back");
                await RollbackAsync(snapshot, log, ct).ConfigureAwait(false);
                throw;
            }

            _ctx.Config.Versions.App = newVersion;
            _ctx.SaveConfig();
            log($"Updated to {newVersion}. The previous version stays in app.old until the next update.");
        }
        finally
        {
            // Also after a failed, rolled-back update: leave the shop with a running Inkspire, not a stopped one.
            if (File.Exists(paths.AppArtisan))
            {
                log("Starting Inkspire again");
                if (wasPhp) await svc.Php.StartAsync(CancellationToken.None).ConfigureAwait(false);
                if (wasNginx) await svc.Nginx.StartAsync(CancellationToken.None).ConfigureAwait(false);
                if (wasScheduler) await svc.Scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task ProvisionNewVersionAsync(Action<string> log, CancellationToken ct)
    {
        var provisioner = new Provisioner(_ctx.Paths, _ctx.Config, log);
        provisioner.EnsureDirectories();
        provisioner.EnsureJunctions();
        provisioner.WriteRuntimeConfigs();
        provisioner.WriteEnv();
        await provisioner.EnsureSqliteAsync(ct).ConfigureAwait(false);
        await provisioner.BootstrapLaravelAsync(null, ct).ConfigureAwait(false); // migrate + caches; never seeds
    }

    private async Task RollbackAsync(string? snapshot, Action<string> log, CancellationToken ct)
    {
        var paths = _ctx.Paths;
        var failed = paths.InstallRoot + Path.DirectorySeparatorChar + "app.failed";
        try
        {
            FolderOps.DeleteTree(failed);
            FolderOps.MoveContents(paths.AppDir, failed);
            FolderOps.MoveContents(paths.AppOldDir, paths.AppDir);
            if (snapshot is not null) SqliteBackup.Restore(paths.SqliteDb, snapshot);

            var provisioner = new Provisioner(paths, _ctx.Config, log);
            provisioner.EnsureJunctions();
            await provisioner.EnsureSqliteAsync(ct).ConfigureAwait(false);
            // The old tree's own caches are still valid, but rebuilding them is cheap insurance against a half-written one.
            await new ArtisanRunner(paths, new PhpRunner(paths, _ctx.Config.Web.MaxRequests)).Optimize(ct, log).ConfigureAwait(false);
            log("The previous version is back, with its database as it was before the update.");
        }
        catch (Exception rb)
        {
            throw new InvalidOperationException(
                $"The update failed AND putting the old version back failed: {rb.Message}. The previous version is in {paths.AppOldDir}" +
                (snapshot is null ? "." : $" and the database backup is {snapshot}."), rb);
        }
    }
}
