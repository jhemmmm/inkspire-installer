namespace Inkspire.Panel.App;

/// <summary>Every absolute path derived from the install root. Nothing else builds paths by hand.</summary>
public sealed class AppPaths
{
    public AppPaths(string installRoot)
    {
        InstallRoot = Path.GetFullPath(installRoot).TrimEnd('\\', '/');
    }

    public string InstallRoot { get; }

    // ---- panel ----
    public string PanelDir => Path.Combine(InstallRoot, "panel");
    public string PanelExe => Path.Combine(PanelDir, "InkspirePanel.exe");
    public string ConfigDir => Path.Combine(InstallRoot, "config");
    public string PanelConfigFile => Path.Combine(ConfigDir, "panel.json");
    public string LogsDir => Path.Combine(InstallRoot, "logs");
    public string PanelLog => Path.Combine(LogsDir, "panel.log");
    public string AdminTaskLog => Path.Combine(LogsDir, "admin-task.log");
    public string PhpErrorLog => Path.Combine(LogsDir, "php-errors.log");
    public string DownloadsDir => Path.Combine(InstallRoot, "downloads");

    // ---- data: everything precious lives here ----
    public string DataDir => Path.Combine(InstallRoot, "data");
    public string SqliteDb => Path.Combine(DataDir, "inkspire.sqlite");
    public string BackupsDir => Path.Combine(DataDir, "backups");
    public string DataTmpDir => Path.Combine(DataDir, "tmp");
    public string DataStorageDir => Path.Combine(DataDir, "storage");
    public string StoragePublicDir => Path.Combine(DataStorageDir, "app", "public");
    public string LaravelLogsDir => Path.Combine(DataStorageDir, "logs");

    /// <summary>Folders Laravel expects under <c>storage\</c>, relative to <see cref="DataStorageDir"/>.</summary>
    public static readonly string[] StorageSkeleton =
    {
        @"app\public", @"app\private", @"framework\cache\data", @"framework\sessions", @"framework\views", "logs", "fonts",
    };

    // ---- php ----
    public string PhpDir => Path.Combine(InstallRoot, "php");
    public string PhpExe => Path.Combine(PhpDir, "php.exe");
    public string PhpCgiExe => Path.Combine(PhpDir, "php-cgi.exe");
    public string PhpIni => Path.Combine(PhpDir, "php.ini");
    public string PhpIniProduction => Path.Combine(PhpDir, "php.ini-production");
    public string PhpExtDir => Path.Combine(PhpDir, "ext");
    public string CaBundle => Path.Combine(PhpDir, "cacert.pem");

    // ---- nginx ----
    public string NginxDir => Path.Combine(InstallRoot, "nginx");
    public string NginxExe => Path.Combine(NginxDir, "nginx.exe");
    public string NginxConf => Path.Combine(NginxDir, "conf", "nginx.conf");
    public string NginxLogsDir => Path.Combine(NginxDir, "logs");
    public string NginxErrorLog => Path.Combine(NginxLogsDir, "error.log");
    public string NginxAccessLog => Path.Combine(NginxLogsDir, "access.log");
    public string NginxTempDir => Path.Combine(NginxDir, "temp");

    // ---- app (Laravel bundle) ----
    public string AppDir => Path.Combine(InstallRoot, "app");
    public string AppNewDir => Path.Combine(InstallRoot, "app.new");
    public string AppOldDir => Path.Combine(InstallRoot, "app.old");
    public string AppPublicDir => Path.Combine(AppDir, "public");
    public string AppEnvFile => Path.Combine(AppDir, ".env");
    public string AppEnvExample => Path.Combine(AppDir, ".env.example");
    public string AppArtisan => Path.Combine(AppDir, "artisan");
    public string AppBootstrapCache => Path.Combine(AppDir, "bootstrap", "cache");
    /// <summary><c>app\storage</c> is a junction to <see cref="DataStorageDir"/>.</summary>
    public string AppStorageLink => Path.Combine(AppDir, "storage");
    /// <summary><c>app\public\storage</c> is a junction to <see cref="StoragePublicDir"/> (what <c>storage:link</c> would make).</summary>
    public string AppPublicStorageLink => Path.Combine(AppPublicDir, "storage");

    /// <summary>Forward-slash form for nginx.conf / php.ini / .env (all three prefer it on Windows).</summary>
    public static string Fwd(string path) => path.Replace('\\', '/');

    public IEnumerable<string> AllRuntimeDirs => new[]
    {
        PanelDir, ConfigDir, LogsDir, DownloadsDir, DataDir, BackupsDir, DataTmpDir, PhpDir, NginxDir, AppDir,
    };
}
