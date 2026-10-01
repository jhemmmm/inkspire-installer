using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.Setup;

public sealed record AdminAccount(string Name, string Email, string Password);

/// <summary>Everything that turns an extracted app folder + the PHP/nginx runtimes into a working install: folders, links,
/// config files, .env, SQLite (WAL), key, migrations, first-run data, caches. Every method is idempotent: the wizard, Repair
/// and the updater all call the same ones.</summary>
public sealed class Provisioner
{
    private readonly AppPaths _paths;
    private readonly PanelConfig _config;
    private readonly Action<string> _log;

    public Provisioner(AppPaths paths, PanelConfig config, Action<string> log)
    {
        _paths = paths;
        _config = config;
        _log = log;
    }

    public void EnsureDirectories()
    {
        foreach (var d in _paths.AllRuntimeDirs) Directory.CreateDirectory(d);
        foreach (var rel in AppPaths.StorageSkeleton) Directory.CreateDirectory(Path.Combine(_paths.DataStorageDir, rel));
        Directory.CreateDirectory(_paths.NginxLogsDir);
        Directory.CreateDirectory(_paths.NginxTempDir);

        // Caches built on the CI machine hold its paths and break artisan on first run here.
        Directory.CreateDirectory(_paths.AppBootstrapCache);
        foreach (var f in Directory.GetFiles(_paths.AppBootstrapCache, "*.php")) File.Delete(f);
        var hot = Path.Combine(_paths.AppPublicDir, "hot");
        if (File.Exists(hot)) File.Delete(hot);
    }

    /// <summary><c>app\storage</c> → <c>data\storage</c> (so an app update replaces code only) and <c>app\public\storage</c> →
    /// <c>data\storage\app\public</c> (what <c>storage:link</c> would do, without needing administrator rights).</summary>
    public void EnsureJunctions()
    {
        LinkFolder(_paths.AppStorageLink, _paths.DataStorageDir);
        Directory.CreateDirectory(_paths.StoragePublicDir);
        LinkFolder(_paths.AppPublicStorageLink, _paths.StoragePublicDir);
    }

    private void LinkFolder(string link, string target)
    {
        if (Junction.PointsTo(link, target)) return;

        if (Junction.IsLink(link))
        {
            Junction.RemoveLink(link);
        }
        else if (Directory.Exists(link))
        {
            // A real folder came with the bundle (or an older install): keep anything in it, then replace it by the link.
            MergeFolder(link, target);
            FolderOps.DeleteTree(link);
        }
        else if (File.Exists(link))
        {
            File.Delete(link);
        }

        Junction.Create(link, target);
        _log($"Linked {link} -> {target}");
    }

    private static void MergeFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            if (!File.Exists(dest)) File.Move(file, dest); // never overwrite data that is already there
        }
    }

    public void WriteRuntimeConfigs()
    {
        _log("Writing nginx.conf");
        ConfigWriter.WriteNginxConf(_config, _paths);
        _log("Writing php.ini");
        ConfigWriter.WritePhpIni(_config, _paths);
    }

    /// <summary>Merges the panel-owned keys (and <paramref name="extra"/>, e.g. mail/payment settings) into .env, creating it
    /// from .env.example when missing. APP_KEY is left alone.</summary>
    public void WriteEnv(IReadOnlyDictionary<string, string?>? extra = null)
    {
        var env = File.Exists(_paths.AppEnvFile) ? EnvFile.Load(_paths.AppEnvFile)
            : File.Exists(_paths.AppEnvExample) ? EnvFile.Load(_paths.AppEnvExample)
            : EnvFile.Empty();

        env.SetAll(ConfigWriter.BaseEnv(_config, _paths));
        if (extra is not null) env.SetAll(extra);

        // .env.example ships MAIL_MAILER=resend. Without a key every scheduled reminder mail would throw.
        if (string.IsNullOrWhiteSpace(env.Get("RESEND_API_KEY")) && env.Get("MAIL_MAILER") == "resend")
            env.Set("MAIL_MAILER", "log");

        env.Save(_paths.AppEnvFile);
        _log("Wrote .env");
    }

    /// <summary>Creates the empty database file and switches it to WAL once (the mode is stored in the file).
    /// WAL lets readers and the single writer work at the same time, which four PCs on one SQLite file need.</summary>
    public async Task EnsureSqliteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.DataDir);
        if (!File.Exists(_paths.SqliteDb))
        {
            File.WriteAllBytes(_paths.SqliteDb, Array.Empty<byte>());
            _log($"Created {_paths.SqliteDb}");
        }

        var db = _paths.SqliteDb.Replace('\\', '/').Replace("'", "\\'");
        var script = $"$p = new PDO('sqlite:{db}'); echo $p->query('PRAGMA journal_mode=WAL')->fetchColumn();";
        var php = new PhpRunner(_paths, _config.Web.MaxRequests);
        var r = await php.RunAsync(
            new[] { "-n", "-d", "extension_dir=" + _paths.PhpExtDir, "-d", "extension=pdo_sqlite", "-d", "extension=sqlite3", "-r", script },
            ct, TimeSpan.FromMinutes(2), _log).ConfigureAwait(false);
        if (!r.Ok || !r.StdOut.Trim().Equals("wal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Could not switch the database to WAL mode: " + r.Combined);
        _log("Database is in WAL mode");
    }

    /// <summary>key:generate (only when APP_KEY is empty) → migrate → first-run data (once) → caches.
    /// <paramref name="admin"/> is only needed on a fresh install; once <see cref="PanelConfig.AppSettings.Seeded"/> is set the
    /// reference data and the administrator are never touched again.</summary>
    public async Task BootstrapLaravelAsync(AdminAccount? admin, CancellationToken ct)
    {
        var php = new PhpRunner(_paths, _config.Web.MaxRequests);
        var artisan = new ArtisanRunner(_paths, php);

        _log("Clearing caches");
        await artisan.OptimizeClear(ct, _log).ConfigureAwait(false); // may fail before a key exists; harmless

        var env = EnvFile.Load(_paths.AppEnvFile);
        if (string.IsNullOrEmpty(env.Get("APP_KEY")))
        {
            _log("Generating APP_KEY");
            ArtisanRunner.Require(await artisan.KeyGenerate(ct, _log).ConfigureAwait(false), "key:generate");
        }

        _log("Running migrations");
        ArtisanRunner.Require(await artisan.Migrate(ct, _log).ConfigureAwait(false), "migrate");

        if (!_config.App.Seeded && admin is not null)
        {
            _log("Creating Inkspire's starting data and the administrator account");
            await RunProvisionScriptAsync(php, admin, ct).ConfigureAwait(false);
            _config.App.Seeded = true;
            _config.App.AdminEmail = admin.Email;
        }

        _log("Building production caches");
        var optimize = await artisan.Optimize(ct, _log).ConfigureAwait(false);
        if (!optimize.Ok)
        {
            // e.g. a route cache problem: config + views are the part that matters for speed.
            _log("optimize failed; caching configuration and views only");
            ArtisanRunner.Require(await artisan.ConfigCache(ct, _log).ConfigureAwait(false), "config:cache");
            await artisan.ViewCache(ct, _log).ConfigureAwait(false);
        }
    }

    /// <summary>Changes the administrator's password (or creates the account) without touching any other data.</summary>
    public Task ResetAdminAsync(AdminAccount admin, CancellationToken ct) =>
        RunProvisionScriptAsync(new PhpRunner(_paths, _config.Web.MaxRequests), admin, ct);

    private async Task RunProvisionScriptAsync(PhpRunner php, AdminAccount admin, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.DataTmpDir);
        var script = Path.Combine(_paths.DataTmpDir, "inkspire-provision.php");
        File.WriteAllText(script, TemplateEngine.ReadResource("provision.php"));
        try
        {
            var env = new Dictionary<string, string>
            {
                ["INKSPIRE_APP_DIR"] = _paths.AppDir,
                ["INKSPIRE_ADMIN_NAME"] = admin.Name,
                ["INKSPIRE_ADMIN_EMAIL"] = admin.Email,
                ["INKSPIRE_ADMIN_PASSWORD"] = admin.Password,
            };
            var r = await php.RunAsync(new[] { script }, ct, TimeSpan.FromMinutes(5), _log, _paths.AppDir, env).ConfigureAwait(false);
            if (!r.Ok || !r.StdOut.Contains("PROVISION_OK"))
                throw new InvalidOperationException("Creating the starting data failed:\n" + r.Combined);
        }
        finally
        {
            try { File.Delete(script); } catch { /* data\tmp is swept anyway */ }
        }
    }
}
