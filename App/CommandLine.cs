namespace Inkspire.Panel.App;

/// <summary>One unit of work for the elevated helper: <c>--admin-task &lt;name&gt; [args...]</c>.</summary>
public sealed record AdminTaskRequest(string Name, IReadOnlyList<string> Args);

/// <summary>Parsed command line. Unknown switches are ignored so a newer shortcut never crashes an older exe.</summary>
public sealed class CommandLine
{
    /// <summary>Force the setup wizard even when an install exists (pre-filled from panel.json).</summary>
    public bool Wizard { get; private set; }
    /// <summary>Started by the HKCU Run key: stay in the tray and start the services.</summary>
    public bool Autostart { get; private set; }
    /// <summary>Bring the main window forward (used after a relaunch).</summary>
    public bool Show { get; private set; }
    public bool Uninstall { get; private set; }
    public string? InstallRootOverride { get; private set; }

    /// <summary>Elevated helper mode.</summary>
    public List<AdminTaskRequest> AdminTasks { get; } = new();
    public string? LogDir { get; private set; }

    // ---- developer switches (headless harnesses) ----
    public string? DevInstallRoot { get; private set; }
    public string? DevPayloadDir { get; private set; }
    public string? DevProvisionRoot { get; private set; }
    public string? DevSmokeRoot { get; private set; }
    public int DevSmokeHoldSeconds { get; private set; } = 5;
    public string? DevAdminEmail { get; private set; }
    public string? DevAppZip { get; private set; }
    public string? DevScreenshotDir { get; private set; }
    public string? DevUpdateRoot { get; private set; }
    public string? DevUpdateZip { get; private set; }
    public string? DevUpdateVersion { get; private set; }
    public int? DevFirewallPort { get; private set; }
    public string? DevFirewallScope { get; private set; }
    public bool DevKeep { get; private set; }

    public static CommandLine Parse(string[] args)
    {
        var cli = new CommandLine();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? Next() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;

            switch (a.ToLowerInvariant())
            {
                case "--wizard": cli.Wizard = true; break;
                case "--autostart": cli.Autostart = true; break;
                case "--show": cli.Show = true; break;
                case "--uninstall": cli.Uninstall = true; break;
                case "--install-root": cli.InstallRootOverride = Next(); break;
                case "--log-dir": cli.LogDir = Next(); break;
                case "--admin-task":
                {
                    var name = Next();
                    if (name is null) break;
                    var taskArgs = new List<string>();
                    while (Next() is { } v) taskArgs.Add(v);
                    cli.AdminTasks.Add(new AdminTaskRequest(name, taskArgs));
                    break;
                }
                case "--dev-install": cli.DevInstallRoot = Next(); break;
                case "--payloads": cli.DevPayloadDir = Next(); break;
                case "--app-zip": cli.DevAppZip = Next(); break;
                case "--admin-email": cli.DevAdminEmail = Next(); break;
                case "--dev-provision": cli.DevProvisionRoot = Next(); break;
                case "--dev-screenshots": cli.DevScreenshotDir = Next(); break;
                case "--dev-firewall":
                    if (Next() is { } fwPort && int.TryParse(fwPort, out var fwp)) cli.DevFirewallPort = fwp;
                    cli.DevFirewallScope = Next();
                    break;
                case "--keep": cli.DevKeep = true; break;
                case "--dev-update":
                    cli.DevUpdateRoot = Next();
                    cli.DevUpdateZip = Next();
                    cli.DevUpdateVersion = Next();
                    break;
                case "--dev-smoke":
                    cli.DevSmokeRoot = Next();
                    if (Next() is { } hold && int.TryParse(hold, out var secs)) cli.DevSmokeHoldSeconds = secs;
                    break;
            }
        }
        return cli;
    }
}
