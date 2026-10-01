using Inkspire.Panel.Config;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.Setup;

/// <summary>All wizard inputs, in memory only. Passwords and API keys go to their final files during the install and never to panel.json.</summary>
public sealed class WizardState
{
    public string InstallRoot { get; set; } = @"C:\Inkspire";
    /// <summary>Opened over an existing install: its database, .env and settings are kept.</summary>
    public bool IsRepair { get; set; }

    // ---- server
    public string ShopName { get; set; } = "Inkspire";
    public int WebPort { get; set; } = 8080;
    public int PoolSize { get; set; } = 6;
    public int PoolBasePort { get; set; } = 9001;
    public bool FirewallEnabled { get; set; } = true;
    public FirewallScope FirewallScope { get; set; } = FirewallScope.LocalSubnet;
    /// <summary>The address other PCs will type; becomes APP_URL. Empty = localhost only.</summary>
    public string LanIp { get; set; } = "";
    /// <summary>An app bundle picked from disk instead of the latest GitHub release.</summary>
    public string? LocalAppZip { get; set; }

    // ---- first administrator
    public string AdminName { get; set; } = "";
    public string AdminEmail { get; set; } = "";
    public string AdminPassword { get; set; } = "";

    // ---- optional integrations
    public string ResendApiKey { get; set; } = "";
    public string MailFromAddress { get; set; } = "";
    public string PaymongoPublicKey { get; set; } = "";
    public string PaymongoSecretKey { get; set; } = "";
    public string PaymongoWebhookSignature { get; set; } = "";

    // ---- resolved while downloading
    public Dictionary<string, Payload> Payloads { get; } = new();
    public Dictionary<string, string> Downloaded { get; } = new();
    public bool NeedVcRedist { get; set; }

    // ---- outcome
    public bool InstallCompleted { get; set; }
    public string? InstallError { get; set; }
    /// <summary>Set when the elevated step could not add the firewall rule (shown on the Finish page).</summary>
    public bool FirewallFailed { get; set; }

    public AdminAccount? Admin =>
        AdminEmail.Length > 0 && AdminPassword.Length > 0 ? new AdminAccount(AdminName.Length > 0 ? AdminName : "Administrator", AdminEmail, AdminPassword) : null;

    public string AppUrl => LanIp.Length > 0 ? $"http://{LanIp}:{WebPort}" : $"http://localhost:{WebPort}";

    public PanelConfig ToPanelConfig(PanelConfig? existing = null)
    {
        var c = existing ?? new PanelConfig();
        c.InstallRoot = InstallRoot;
        c.InstalledAtUtc ??= DateTime.UtcNow;
        c.App.Name = ShopName;
        c.App.AppUrl = AppUrl;
        if (AdminEmail.Length > 0) c.App.AdminEmail = AdminEmail;
        c.Web.Port = WebPort;
        c.Web.PoolSize = PoolSize;
        c.Web.PoolBasePort = PoolBasePort;
        c.Firewall.Enabled = FirewallEnabled;
        c.Firewall.Scope = FirewallScope;
        foreach (var (id, p) in Payloads)
        {
            switch (id)
            {
                case "php": c.Versions.Php = p.Version; break;
                case "nginx": c.Versions.Nginx = p.Version; break;
                case "app": c.Versions.App = p.Version; break;
            }
        }
        c.Versions.Panel = typeof(WizardState).Assembly.GetName().Version?.ToString(3) ?? "";
        return c;
    }

    /// <summary>.env keys derived from the integrations page. Empty fields change nothing, so a repair never wipes a saved key.</summary>
    public IReadOnlyDictionary<string, string?> IntegrationEnv()
    {
        var d = new Dictionary<string, string?>();
        if (ResendApiKey.Length > 0)
        {
            d["MAIL_MAILER"] = "resend";
            d["RESEND_API_KEY"] = ResendApiKey;
            if (MailFromAddress.Length > 0) d["MAIL_FROM_ADDRESS"] = MailFromAddress;
        }
        if (PaymongoPublicKey.Length > 0) d["PAYMONGO_PUBLIC_KEY"] = PaymongoPublicKey;
        if (PaymongoSecretKey.Length > 0)
        {
            d["PAYMONGO_SECRET_KEY"] = PaymongoSecretKey;
            d["PAYMONGO_LIVEMODE"] = PaymongoSecretKey.StartsWith("sk_live_", StringComparison.Ordinal) ? "true" : "false";
        }
        if (PaymongoWebhookSignature.Length > 0) d["PAYMONGO_WEBHOOK_SIG"] = PaymongoWebhookSignature;
        return d;
    }

    /// <summary>Pre-fill from an existing install (Re-run setup / Repair).</summary>
    public static WizardState FromExisting(PanelConfig c, EnvFile? env)
    {
        var s = new WizardState
        {
            InstallRoot = c.InstallRoot,
            IsRepair = true,
            ShopName = c.App.Name,
            WebPort = c.Web.Port,
            PoolSize = c.Web.PoolSize,
            PoolBasePort = c.Web.PoolBasePort,
            FirewallEnabled = c.Firewall.Enabled,
            FirewallScope = c.Firewall.Scope,
            AdminEmail = c.App.AdminEmail,
            LanIp = Uri.TryCreate(c.App.AppUrl, UriKind.Absolute, out var u) && u.Host != "localhost" ? u.Host : "",
        };
        if (env is not null)
        {
            s.MailFromAddress = env.Get("MAIL_FROM_ADDRESS") ?? "";
            s.PaymongoPublicKey = env.Get("PAYMONGO_PUBLIC_KEY") ?? "";
        }
        return s;
    }
}
