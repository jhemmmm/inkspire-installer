using System.Text;
using Inkspire.Panel.App;
using Inkspire.Panel.Config;

namespace Inkspire.Panel.Runtime;

/// <summary>Renders nginx.conf and php.ini and defines the .env keys the panel owns. The single place the wizard, Settings
/// and the updater use, so the files are always regenerated the same way from <see cref="PanelConfig"/>.</summary>
public static class ConfigWriter
{
    /// <summary>Shared libraries Inkspire's dependencies need (dompdf: gd/mbstring/zip; openspout: zip; mail/payments: curl+openssl).
    /// dom/xml/xmlreader/xmlwriter/zlib are compiled into the Windows PHP build.</summary>
    public static readonly string[] PhpExtensions =
    {
        "curl", "fileinfo", "gd", "intl", "mbstring", "openssl", "pdo_sqlite", "sodium", "sqlite3", "zip",
    };

    public static string RenderNginxConf(PanelConfig config, AppPaths paths)
    {
        var upstream = new StringBuilder();
        for (var i = 0; i < config.Web.PoolSize; i++)
            // max_fails=0: never "mark the worker down". They are local and a recycled/restarted one is back in ~100 ms;
            // nginx simply retries the next worker when a connection is refused, so a restart never leaves "no live upstreams".
            upstream.Append($"        server 127.0.0.1:{config.Web.PoolBasePort + i} max_fails=0;\n");

        var values = new Dictionary<string, string>
        {
            ["PORT"] = config.Web.Port.ToString(),
            ["APP_PUBLIC"] = AppPaths.Fwd(paths.AppPublicDir),
            ["MAX_BODY_MB"] = config.Web.MaxUploadMb.ToString(),
            ["UPSTREAM_SERVERS"] = upstream.ToString().TrimEnd('\n'),
        };
        return TemplateEngine.RenderResource("nginx.conf.tmpl", values);
    }

    public static void WriteNginxConf(PanelConfig config, AppPaths paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.NginxConf)!);
        File.WriteAllText(paths.NginxConf, RenderNginxConf(config, paths), new UTF8Encoding(false));
        Directory.CreateDirectory(paths.NginxLogsDir);
        Directory.CreateDirectory(paths.NginxTempDir);
    }

    public static PhpIniPatcher BuildPhpIni(PanelConfig config, AppPaths paths, string baseIni)
    {
        var opcacheDir = Path.Combine(paths.PhpDir, "opcache");
        var maxUpload = config.Web.MaxUploadMb;

        var patcher = new PhpIniPatcher(baseIni)
            .Set("extension_dir", Q(paths.PhpExtDir))
            .Set("memory_limit", "512M")
            .Set("upload_max_filesize", $"{maxUpload}M")
            .Set("post_max_size", $"{maxUpload + 4}M")
            .Set("max_execution_time", "300")
            .Set("max_input_time", "300")
            // Laravel sets app.timezone (UTC, by design) itself; this only makes native date() agree.
            .Set("date.timezone", "UTC")
            .Set("expose_php", "Off")
            .Set("display_errors", "Off")
            .Set("display_startup_errors", "Off")
            .Set("log_errors", "On")
            .Set("error_log", Q(paths.PhpErrorLog))
            .Set("cgi.force_redirect", "0")
            .Set("cgi.fix_pathinfo", "0")
            .Set("realpath_cache_size", "4096K")
            .Set("realpath_cache_ttl", "600")
            .Set("curl.cainfo", Q(paths.CaBundle))
            .Set("openssl.cafile", Q(paths.CaBundle))
            .Set("sys_temp_dir", Q(paths.DataTmpDir))
            .Set("opcache.enable", "1")
            .Set("opcache.enable_cli", "0")
            .Set("opcache.memory_consumption", "128")
            .Set("opcache.interned_strings_buffer", "16")
            .Set("opcache.max_accelerated_files", "20000")
            .Set("opcache.validate_timestamps", "1")
            .Set("opcache.revalidate_freq", "2")
            // Survives php-cgi recycling (PHP_FCGI_MAX_REQUESTS), so a respawned worker does not recompile Laravel.
            .Set("opcache.file_cache", Q(opcacheDir))
            .Set("opcache.file_cache_fallback", "1")
            .Set("opcache.jit", "off")
            .Set("opcache.jit_buffer_size", "0");

        foreach (var ext in PhpExtensions) patcher.EnableExtension(ext);
        patcher.EnableZendExtension("opcache");
        return patcher;
    }

    public static void WritePhpIni(PanelConfig config, AppPaths paths)
    {
        var source = File.Exists(paths.PhpIniProduction) ? paths.PhpIniProduction : Path.Combine(paths.PhpDir, "php.ini-development");
        var baseIni = File.Exists(source) ? File.ReadAllText(source) : "[PHP]\n";
        Directory.CreateDirectory(Path.Combine(paths.PhpDir, "opcache"));
        Directory.CreateDirectory(paths.LogsDir);
        Directory.CreateDirectory(paths.DataTmpDir);
        BuildPhpIni(config, paths, baseIni).Save(paths.PhpIni);
    }

    /// <summary>The address the app should believe it lives at (used by scheduler mails and Storage URLs).</summary>
    public static string AppUrl(PanelConfig config) =>
        string.IsNullOrWhiteSpace(config.App.AppUrl) ? $"http://localhost:{config.Web.Port}" : config.App.AppUrl.TrimEnd('/');

    /// <summary>.env keys the panel owns; everything else in .env is the user's. APP_KEY is generated once, never here.</summary>
    public static IReadOnlyDictionary<string, string?> BaseEnv(PanelConfig config, AppPaths paths) => new Dictionary<string, string?>
    {
        ["APP_NAME"] = config.App.Name,
        ["APP_ENV"] = "production",
        ["APP_DEBUG"] = "false",
        ["APP_URL"] = AppUrl(config),
        ["LOG_CHANNEL"] = "daily",
        ["LOG_LEVEL"] = "warning",
        ["LOG_DAILY_DAYS"] = "14",
        ["DB_CONNECTION"] = "sqlite",
        ["DB_DATABASE"] = AppPaths.Fwd(paths.SqliteDb),
        ["DB_FOREIGN_KEYS"] = "true",
        // One SQLite file serves four PCs: keep the hot, tiny writes (sessions, cache) out of it.
        ["SESSION_DRIVER"] = "file",
        ["CACHE_STORE"] = "file",
        // Inkspire dispatches no queued jobs; sync means nothing can pile up unprocessed without a worker.
        ["QUEUE_CONNECTION"] = "sync",
        ["BROADCAST_CONNECTION"] = "log",
        ["FILESYSTEM_DISK"] = "local",
    };

    private static string Q(string path) => "\"" + AppPaths.Fwd(path) + "\"";
}
