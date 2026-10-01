using Inkspire.Panel.App;

namespace Inkspire.Panel.Runtime;

public sealed record UpdateInfo(string Installed, GitHubRelease? Latest, bool Available, string? Error)
{
    public string LatestTag => Latest?.Tag ?? "";
}

/// <summary>Looks up the newest Inkspire release on GitHub. Never installs anything by itself.</summary>
public static class UpdateChecker
{
    public static async Task<UpdateInfo> CheckAsync(AppServices ctx, CancellationToken ct)
    {
        var installed = ctx.Config.Versions.App;
        try
        {
            var latest = await GitHubReleases.LatestAppAsync(ctx.Http, ct).ConfigureAwait(false);
            ctx.Config.Updates.LastCheckUtc = DateTime.UtcNow;
            ctx.SaveConfig();
            if (latest is null) return new UpdateInfo(installed, null, false, "No release with an inkspire-app zip has been published yet.");
            var available = installed.Length == 0 || GitHubReleases.IsNewer(installed, latest.Tag);
            return new UpdateInfo(installed, latest, available, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UpdateInfo(installed, null, false, "Could not reach GitHub: " + ex.Message);
        }
    }

    /// <summary>True when the daily lookup is due.</summary>
    public static bool IsDue(AppServices ctx)
    {
        var u = ctx.Config.Updates;
        if (!u.AutoCheck) return false;
        return u.LastCheckUtc is null || DateTime.UtcNow - u.LastCheckUtc.Value >= TimeSpan.FromHours(Math.Max(1, u.CheckIntervalHours));
    }
}
