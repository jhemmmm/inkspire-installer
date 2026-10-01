using Inkspire.Panel.App;
using Inkspire.Panel.Config;

namespace Inkspire.Panel.Runtime;

/// <summary>Online SQLite backup through PHP's <c>SQLite3::backup()</c> (consistent while nginx/php-cgi are writing), then an
/// integrity check, a copy of <c>.env</c> (APP_KEY!) and retention pruning.
/// Files: <c>data\backups\inkspire-YYYYMMDD-HHmmss.sqlite</c> + <c>.env</c>. Pre-update snapshots use the prefix
/// <c>pre-update-</c> and are never pruned automatically.</summary>
public sealed class SqliteBackup
{
    public const string ScheduledPrefix = "inkspire-";
    public const string PreUpdatePrefix = "pre-update-";

    private readonly AppPaths _paths;
    private readonly PanelConfig _config;

    public SqliteBackup(AppPaths paths, PanelConfig config)
    {
        _paths = paths;
        _config = config;
    }

    /// <param name="prefix">File-name prefix; <see cref="ScheduledPrefix"/> for normal backups.</param>
    public async Task<string> RunAsync(CancellationToken ct, Action<string>? log = null, string prefix = ScheduledPrefix)
    {
        if (!File.Exists(_paths.SqliteDb)) throw new FileNotFoundException("There is no database to back up yet.", _paths.SqliteDb);
        Directory.CreateDirectory(_paths.BackupsDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(_paths.BackupsDir, $"{prefix}{stamp}.sqlite");

        var php = new PhpRunner(_paths, _config.Web.MaxRequests);
        var script = BackupScript(_paths.SqliteDb, target);
        var r = await php.RunAsync(
            new[] { "-n", "-d", "extension_dir=" + _paths.PhpExtDir, "-d", "extension=sqlite3", "-r", script },
            ct, TimeSpan.FromMinutes(10), log).ConfigureAwait(false);
        if (!r.Ok || !r.StdOut.Contains("BACKUP_OK"))
        {
            try { if (File.Exists(target)) File.Delete(target); } catch { /* best effort */ }
            throw new InvalidOperationException("Backup failed: " + r.Combined);
        }

        if (File.Exists(_paths.AppEnvFile))
            File.Copy(_paths.AppEnvFile, Path.ChangeExtension(target, ".env"), overwrite: true);

        log?.Invoke($"Backup written: {target} ({new FileInfo(target).Length / 1024} KB)");
        if (prefix == ScheduledPrefix)
        {
            Prune(log);
            _config.Backup.LastRunUtc = DateTime.UtcNow;
        }
        return target;
    }

    private static string BackupScript(string source, string target)
    {
        static string P(string s) => s.Replace("\\", "/").Replace("'", "\\'");
        return
            "$src = new SQLite3('" + P(source) + "', SQLITE3_OPEN_READONLY);" +
            "$src->busyTimeout(10000);" +
            "$dst = new SQLite3('" + P(target) + "');" +
            "if (!$src->backup($dst)) { fwrite(STDERR, 'backup() returned false'); exit(2); }" +
            // The copy inherits WAL mode; a backup must be ONE self-contained file, not a trio with -wal/-shm beside it.
            "$dst->exec('PRAGMA journal_mode=DELETE');" +
            "$dst->close();" +
            "$chk = new SQLite3('" + P(target) + "', SQLITE3_OPEN_READONLY);" +
            "$res = $chk->querySingle('PRAGMA integrity_check');" +
            "if ($res !== 'ok') { fwrite(STDERR, 'integrity_check: ' . $res); exit(3); }" +
            "echo 'BACKUP_OK';";
    }

    public void Prune(Action<string>? log = null)
    {
        var keep = Math.Max(1, _config.Backup.RetentionCount);
        foreach (var old in List().Skip(keep))
        {
            try
            {
                old.Delete();
                var env = Path.ChangeExtension(old.FullName, ".env");
                if (File.Exists(env)) File.Delete(env);
                log?.Invoke($"Pruned old backup {old.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not prune {old.FullName}: {ex.Message}");
            }
        }
    }

    /// <summary>Replaces <paramref name="databaseFile"/> with a backup. The -wal/-shm files belong to the old database and must go first,
    /// or SQLite would replay them into the restored file. Stop the app before calling this.</summary>
    public static void Restore(string databaseFile, string backupFile)
    {
        foreach (var sidecar in new[] { databaseFile + "-wal", databaseFile + "-shm" })
        {
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
        File.Copy(backupFile, databaseFile, overwrite: true);
    }

    public bool IsDue()
    {
        if (!_config.Backup.Enabled) return false;
        var last = _config.Backup.LastRunUtc;
        return last is null || DateTime.UtcNow - last.Value >= TimeSpan.FromHours(Math.Max(1, _config.Backup.IntervalHours));
    }

    /// <summary>Scheduled backups, newest first.</summary>
    public IReadOnlyList<FileInfo> List() =>
        Directory.Exists(_paths.BackupsDir)
            ? new DirectoryInfo(_paths.BackupsDir).GetFiles(ScheduledPrefix + "*.sqlite").OrderByDescending(f => f.Name).ToList()
            : Array.Empty<FileInfo>();
}
