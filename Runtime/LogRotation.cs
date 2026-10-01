using Inkspire.Panel.App;
using Inkspire.Panel.Services;

namespace Inkspire.Panel.Runtime;

/// <summary>nginx on Windows never rotates its logs, and the dashboards' 5-second polling writes thousands of access-log lines a day.
/// This keeps a few size-limited generations and asks nginx to reopen the file.</summary>
public static class LogRotation
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int Keep = 5;

    /// <summary>Rotates <paramref name="file"/> when it is over <paramref name="maxBytes"/>. True when it did.</summary>
    public static bool RotateIfLarge(string file, long maxBytes = MaxBytes, int keep = Keep)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length < maxBytes) return false;

        var oldest = $"{file}.{keep}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var i = keep - 1; i >= 1; i--)
        {
            var from = $"{file}.{i}";
            if (File.Exists(from)) File.Move(from, $"{file}.{i + 1}");
        }
        // nginx holds the file open but allows renames (it opens with delete sharing).
        File.Move(file, $"{file}.1");
        return true;
    }

    public static async Task RotateNginxAsync(AppServices ctx)
    {
        var rotated = false;
        try
        {
            rotated |= RotateIfLarge(ctx.Paths.NginxAccessLog);
            rotated |= RotateIfLarge(ctx.Paths.NginxErrorLog);
        }
        catch (Exception ex)
        {
            Log.Warn("Log rotation failed: " + ex.Message);
            return;
        }
        if (rotated && ctx.Services.Nginx.Status.IsActive)
        {
            try { await ctx.Services.Nginx.ReopenLogsAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Warn("nginx could not reopen its logs: " + ex.Message); }
        }
    }
}
