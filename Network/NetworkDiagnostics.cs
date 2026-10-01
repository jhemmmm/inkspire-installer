using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.Network;

/// <summary>"Why can't the other PCs connect?" in one list: is nginx listening for other PCs, does the firewall let them in,
/// is something blocking, which addresses to type, is the app answering. Shared by the Network &amp; Clients dialog and the wizard.</summary>
public static class NetworkDiagnostics
{
    public static async Task<IReadOnlyList<CheckResult>> RunAsync(AppServices ctx, CancellationToken ct)
    {
        var config = ctx.Config;
        var port = config.Web.Port;
        var results = new List<CheckResult>();

        // 1. nginx up and bound to every interface (a loopback-only listener could never be reached from another PC)
        var nginxActive = ctx.Services.Nginx.Status.IsActive;
        if (!nginxActive)
            results.Add(new("Web server", CheckLevel.Warning, "Inkspire is stopped. Start it, then check again."));
        else if (PortProbe.IsListeningOnAllInterfaces(port))
            results.Add(new("Web server", CheckLevel.Ok, $"nginx accepts connections from other PCs on port {port}."));
        else
            results.Add(new("Web server", CheckLevel.Blocker, $"Nothing is listening for other PCs on port {port}. Check the nginx log."));

        // 2. Windows Firewall
        results.AddRange(FirewallChecks(config, ctx.Paths));

        // 3. networks Windows is connected to
        var profiles = NetworkProfiles.Connected();
        if (profiles.Count > 0)
        {
            var text = string.Join(", ", profiles.Select(p => $"{p.Name} ({p.Category})"));
            results.Add(new("Network type", CheckLevel.Info,
                text + (profiles.Any(p => p.Category == "Public") ? ". Windows calls it Public; the firewall rule covers Public networks too." : "")));
        }

        // 4. addresses
        var addresses = LanAddresses.List();
        if (addresses.Count == 0)
        {
            results.Add(new("Address of this PC", CheckLevel.Blocker, "No network address found. Connect this PC to the shop network (cable or Wi-Fi)."));
        }
        else
        {
            foreach (var a in addresses)
            {
                results.Add(a.IsDhcp
                    ? new("Address of this PC", CheckLevel.Warning,
                        $"{a.Ip} ({a.Adapter}) is handed out by DHCP and can change after a restart. Reserve it for this PC in the router so bookmarks keep working.")
                    : new("Address of this PC", CheckLevel.Ok, $"{a.Ip} ({a.Adapter}, fixed)"));
            }
            var primary = addresses[0].Ip;
            var configured = HostOf(config.App.AppUrl);
            var localOnly = configured is "" or "localhost" or "127.0.0.1";
            if (localOnly && config.Firewall.Enabled)
            {
                // The panel opens the firewall for the other PCs, yet Inkspire builds its own links (emails, shared file
                // links, avatars) from APP_URL.
                results.Add(new("APP_URL", CheckLevel.Warning,
                    $"Inkspire's address is set to this PC only (localhost). Links in emails and shared files would not work on the other PCs. Its LAN address is {primary}.", "appurl"));
            }
            else if (!localOnly && System.Net.IPAddress.TryParse(configured, out _) && addresses.All(a => a.Ip != configured))
            {
                results.Add(new("APP_URL", CheckLevel.Warning,
                    $"Inkspire still believes this PC is {configured}, but its address is now {primary}. Emails and file links would point to the old one.", "appurl"));
            }
        }

        // 5. does the app answer, locally and on the LAN address
        if (nginxActive)
        {
            results.Add(await ProbeAsync(ctx, "http://127.0.0.1:" + port, "App on this PC", ct).ConfigureAwait(false));
            if (addresses.Count > 0)
                results.Add(await ProbeAsync(ctx, $"http://{addresses[0].Ip}:{port}", $"App via {addresses[0].Ip}", ct).ConfigureAwait(false));
        }
        return results;
    }

    private static IEnumerable<CheckResult> FirewallChecks(PanelConfig config, AppPaths paths)
    {
        var port = config.Web.Port;
        if (!config.Firewall.Enabled)
        {
            yield return new("Windows Firewall", CheckLevel.Info, "The panel is set not to manage the firewall (Settings). The other PCs can only connect if you allowed the port yourself.");
            yield break;
        }

        var status = FirewallManager.Inspect(port, paths.NginxExe, config.Firewall.Scope);
        if (status.Error is not null)
        {
            yield return new("Windows Firewall", CheckLevel.Info, "Could not read the firewall settings: " + status.Error);
            yield break;
        }
        if (!status.ServiceRunning)
        {
            yield return new("Windows Firewall", CheckLevel.Info,
                $"The Windows Firewall service is not running (another security product may be in charge). If the other PCs cannot connect, allow TCP port {port} there.");
            yield break;
        }

        if (status.BlockingRules.Count > 0)
        {
            yield return new("Firewall block rule", CheckLevel.Blocker,
                "Windows has a rule that blocks nginx or this port (usually from clicking Cancel on the \"Allow access\" prompt): "
                + string.Join(", ", status.BlockingRules.Select(r => r.Name).Distinct()) + ". Block rules beat allow rules.", "unblock");
        }

        if (status.WebRule is null)
            yield return new("Firewall rule", CheckLevel.Blocker, $"No rule lets other PCs reach port {port}. They will time out until it is added.", "firewall");
        else if (!FirewallManager.RuleMatches(status.WebRule, port, config.Firewall.Scope))
            yield return new("Firewall rule", CheckLevel.Blocker, $"The rule \"{status.WebRule.Name}\" exists but is disabled or no longer matches the settings (port, scope, networks).", "firewall");
        else
            yield return new("Firewall rule", CheckLevel.Ok, $"\"{status.WebRule.Name}\": allows {DescribeScope(config.Firewall.Scope)} to reach port {port} on every network type.");
    }

    public static string DescribeScope(FirewallScope scope) => scope switch
    {
        FirewallScope.Any => "any address",
        FirewallScope.PrivateRanges => "private addresses (10.x, 172.16-31.x, 192.168.x)",
        _ => "PCs on the same subnet",
    };

    private static async Task<CheckResult> ProbeAsync(AppServices ctx, string baseUrl, string name, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            using var response = await ctx.Http.GetAsync(baseUrl + "/up", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return (int)response.StatusCode is >= 200 and < 300
                ? new CheckResult(name, CheckLevel.Ok, $"{baseUrl}/ answered HTTP {(int)response.StatusCode}.")
                : new CheckResult(name, CheckLevel.Warning, $"{baseUrl}/ answered HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new CheckResult(name, CheckLevel.Warning, $"{baseUrl}/ did not answer: {ex.Message}");
        }
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

    /// <summary>Instructions to hand to the other PCs, as plain text.</summary>
    public static string ClientInstructions(PanelConfig config, IReadOnlyList<LanAddress> addresses)
    {
        var port = config.Web.Port;
        var lines = new List<string>
        {
            $"Open {config.App.Name} on this computer",
            "",
            "1. Make sure the computer is connected to the shop network (same router or Wi-Fi as the server PC).",
            "2. Open Chrome, Edge or Firefox and type one of these addresses:",
        };
        foreach (var a in addresses) lines.Add($"      http://{a.Ip}:{port}/");
        lines.Add($"      http://{Environment.MachineName}:{port}/   (the server PC's name; works on most Windows PCs)");
        lines.Add("3. Log in with your own account. Each person needs their own login: a second login on the same account signs the first one out.");
        lines.Add("4. Bookmark the page, or copy the Inkspire.url shortcut to the desktop.");
        lines.Add("");
        lines.Add("If the page does not open:");
        lines.Add("  - Check the server PC is on and Inkspire Control Panel shows green.");
        lines.Add($"  - On this computer run in PowerShell:  Test-NetConnection {addresses.FirstOrDefault()?.Ip ?? "<server address>"} -Port {port}");
        lines.Add("    TcpTestSucceeded : True means the network is fine and it is a browser/address problem.");
        lines.Add("    False means a firewall, Wi-Fi \"client isolation\", or a different network is in the way.");
        lines.Add("  - If the server PC's address changed, ask whoever manages it for the new one.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Contents of an Inkspire.url Internet Shortcut.</summary>
    public static string UrlShortcut(string url) => $"[InternetShortcut]\r\nURL={url}\r\n";
}
