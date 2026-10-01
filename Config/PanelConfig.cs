namespace Inkspire.Panel.Config;

/// <summary>Who may reach the web port through Windows Firewall.</summary>
public enum FirewallScope
{
    /// <summary>Only devices on the same subnet as this PC (default).</summary>
    LocalSubnet,
    /// <summary>Any RFC 1918 address (10/8, 172.16/12, 192.168/16): shops whose PCs sit on different subnets.</summary>
    PrivateRanges,
    /// <summary>Every address, including the internet if the router forwards it. Not recommended.</summary>
    Any,
}

/// <summary>Persisted panel configuration (<c>config\panel.json</c>). Secrets (API keys) live in the app's .env, not here.</summary>
public sealed class PanelConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string InstallRoot { get; set; } = "";
    public DateTime? InstalledAtUtc { get; set; }

    public WebSettings Web { get; set; } = new();
    public FirewallSettings Firewall { get; set; } = new();
    public AppSettings App { get; set; } = new();
    public ServiceSettings Services { get; set; } = new();
    public BackupSettings Backup { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public VersionInfo Versions { get; set; } = new();

    public sealed class WebSettings
    {
        /// <summary>nginx listen port (0.0.0.0). 8080 rather than 80: IIS, Skype, XAMPP and Laragon like port 80.</summary>
        public int Port { get; set; } = 8080;
        public int PoolSize { get; set; } = 6;
        public int PoolBasePort { get; set; } = 9001;
        /// <summary>PHP_FCGI_MAX_REQUESTS: a php-cgi worker exits cleanly after this many requests and is respawned. 0 = never.
        /// Every recycle leaves a ~100 ms gap in which that worker refuses connections, so this is high on purpose (PHP's own
        /// default is 500, which would recycle each worker every few minutes under the dashboards' 5-second polling).</summary>
        public int MaxRequests { get; set; } = 10000;
        /// <summary>nginx client_max_body_size and PHP upload limits, in MB. The app's own cap (default 50) sits below.</summary>
        public int MaxUploadMb { get; set; } = 256;
    }

    public sealed class FirewallSettings
    {
        /// <summary>Open the web port in Windows Firewall so the other PCs can connect.</summary>
        public bool Enabled { get; set; } = true;
        public FirewallScope Scope { get; set; } = FirewallScope.LocalSubnet;
    }

    public sealed class AppSettings
    {
        public string Name { get; set; } = "Inkspire";
        public string AdminEmail { get; set; } = "";
        /// <summary>Value of APP_URL, e.g. http://192.168.1.50:8080. Empty until the wizard picks a LAN address.</summary>
        public string AppUrl { get; set; } = "";
        /// <summary>Reference data + the first administrator were created. Seeders must never run again (they reset prices and settings).</summary>
        public bool Seeded { get; set; }
    }

    public sealed class ServiceToggle
    {
        public bool Enabled { get; set; } = true;
        /// <summary>Started automatically when the panel launches.</summary>
        public bool Autostart { get; set; } = true;
    }

    public sealed class ServiceSettings
    {
        public ServiceToggle Nginx { get; set; } = new();
        public ServiceToggle Php { get; set; } = new();
        public ServiceToggle Scheduler { get; set; } = new();

        public ServiceToggle Get(string id) => id switch
        {
            "nginx" => Nginx,
            "php" => Php,
            "scheduler" => Scheduler,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown service id"),
        };
    }

    public sealed class BackupSettings
    {
        public bool Enabled { get; set; } = true;
        public int RetentionCount { get; set; } = 14;
        public int IntervalHours { get; set; } = 24;
        public DateTime? LastRunUtc { get; set; }
    }

    public sealed class UpdateSettings
    {
        /// <summary>Look up GitHub releases/latest once per interval and tell the user (never installs by itself).</summary>
        public bool AutoCheck { get; set; } = true;
        public int CheckIntervalHours { get; set; } = 24;
        public DateTime? LastCheckUtc { get; set; }
        /// <summary>Tag the tray already announced, so the balloon shows once per release.</summary>
        public string NotifiedTag { get; set; } = "";
    }

    public sealed class UiSettings
    {
        public bool RunAtLogin { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool ConfirmStopOnExit { get; set; } = true;
        /// <summary>Stop Windows from sleeping while the services run, so the other PCs keep working.</summary>
        public bool KeepAwake { get; set; } = true;
    }

    public sealed class VersionInfo
    {
        public string Php { get; set; } = "";
        public string Nginx { get; set; } = "";
        public string App { get; set; } = "";
        public string Panel { get; set; } = "";
    }

    /// <summary>Clamp values a hand-edited panel.json could have broken.</summary>
    public void Normalize()
    {
        Web.Port = Math.Clamp(Web.Port, 1, 65535);
        Web.PoolSize = Math.Clamp(Web.PoolSize, 1, 32);
        Web.PoolBasePort = Math.Clamp(Web.PoolBasePort, 1024, 65535 - 32);
        Web.MaxRequests = Web.MaxRequests <= 0 ? 0 : Math.Clamp(Web.MaxRequests, 50, 1_000_000);
        Web.MaxUploadMb = Math.Clamp(Web.MaxUploadMb, 8, 2048);
        Backup.RetentionCount = Math.Clamp(Backup.RetentionCount, 1, 365);
        Backup.IntervalHours = Math.Clamp(Backup.IntervalHours, 1, 24 * 30);
        Updates.CheckIntervalHours = Math.Clamp(Updates.CheckIntervalHours, 1, 24 * 30);
    }
}
