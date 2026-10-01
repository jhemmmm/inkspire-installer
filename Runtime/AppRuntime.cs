using Inkspire.Panel.App;

namespace Inkspire.Panel.Runtime;

/// <summary>Applies changes to .env / php.ini / nginx.conf to a RUNNING install: rebuild Laravel's caches (it reads a cached
/// config, not .env), then restart the PHP workers one by one so nginx always has a live upstream.</summary>
public static class AppRuntime
{
    public static async Task ReloadAsync(AppServices ctx, Action<string> log, CancellationToken ct)
    {
        var artisan = new ArtisanRunner(ctx.Paths, new PhpRunner(ctx.Paths, ctx.Config.Web.MaxRequests));

        log("Clearing caches");
        ArtisanRunner.Require(await artisan.OptimizeClear(ct, log).ConfigureAwait(false), "optimize:clear");

        log("Rebuilding caches");
        var optimize = await artisan.Optimize(ct, log).ConfigureAwait(false);
        if (!optimize.Ok)
        {
            log("optimize failed; caching configuration and views only");
            ArtisanRunner.Require(await artisan.ConfigCache(ct, log).ConfigureAwait(false), "config:cache");
            await artisan.ViewCache(ct, log).ConfigureAwait(false);
        }

        if (ctx.Services.Php.Status.IsActive)
        {
            log("Restarting the PHP workers one by one");
            await ctx.Services.Php.RollingRestartAsync(ct).ConfigureAwait(false);
        }
        if (ctx.Services.Scheduler.Status.IsActive)
        {
            log("Restarting the scheduler");
            await ctx.Services.Scheduler.RestartAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Points APP_URL at <paramref name="url"/> (the address other PCs use) and reloads.</summary>
    public static async Task ChangeAppUrlAsync(AppServices ctx, string url, Action<string> log, CancellationToken ct)
    {
        ctx.Config.App.AppUrl = url.TrimEnd('/');
        ctx.SaveConfig();
        var env = EnvFile.Load(ctx.Paths.AppEnvFile);
        env.Set("APP_URL", ConfigWriter.AppUrl(ctx.Config));
        env.Save(ctx.Paths.AppEnvFile);
        log($"APP_URL is now {ConfigWriter.AppUrl(ctx.Config)}");
        await ReloadAsync(ctx, log, ct).ConfigureAwait(false);
    }
}
