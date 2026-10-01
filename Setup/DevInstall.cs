using Inkspire.Panel.App;

namespace Inkspire.Panel.Setup;

/// <summary><c>InkspirePanel.exe --dev-install &lt;root&gt; [--payloads &lt;dir&gt;] [--app-zip &lt;bundle.zip&gt;] [--admin-email x]</c>:
/// the real <see cref="InstallEngine"/> without the wizard, without UAC and without registering anything (no Run key,
/// shortcut or pointer file), so a scratch install never hijacks the real one. Files found in <c>--payloads</c> are used
/// instead of the internet. The admin password comes from INKSPIRE_DEV_ADMIN_PASSWORD. Exit code 0 = success.</summary>
public static class DevInstall
{
    public static int Run(CommandLine cli)
    {
        ConsoleAttach.Attach();
        var root = cli.DevInstallRoot!;
        var state = new WizardState
        {
            InstallRoot = Path.GetFullPath(root),
            ShopName = "Inkspire Dev",
            FirewallEnabled = false,
            LocalAppZip = cli.DevAppZip,
            AdminName = "Dev Admin",
            AdminEmail = cli.DevAdminEmail ?? "dev-admin@example.com",
            AdminPassword = Environment.GetEnvironmentVariable("INKSPIRE_DEV_ADMIN_PASSWORD") ?? "Dev-Password-2026!",
        };
        var paths = new AppPaths(state.InstallRoot);
        Directory.CreateDirectory(paths.LogsDir);
        Log.Configure(paths.LogsDir);
        state.IsRepair = File.Exists(paths.PanelConfigFile);

        void Say(string line)
        {
            Console.WriteLine(line);
            Log.Info(line);
        }

        try
        {
            using var http = AppServices.CreateHttpClient();
            Say("Resolving payloads…");
            var plan = PayloadPlan.BuildAsync(state, http, CancellationToken.None, cli.DevPayloadDir).GetAwaiter().GetResult();
            var downloads = new DownloadManager(http, paths.DownloadsDir);
            foreach (var p in plan)
            {
                state.Payloads[p.Id] = p;
                Say($"  {p.Title}");
                state.Downloaded[p.Id] = downloads.FetchAsync(p, null, CancellationToken.None).GetAwaiter().GetResult();
            }

            var engine = new InstallEngine(state, new Progress<StepProgress>(p => { if (p.Message.Length == 0) Say($"[{p.Step}]"); }), Say)
            {
                SkipRegistration = true,
                SkipElevation = true,
            };
            engine.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            Say("INSTALL DONE");
            return 0;
        }
        catch (Exception ex)
        {
            Say("INSTALL FAILED: " + ex.Message);
            Log.Error("dev-install failed", ex);
            return 1;
        }
    }
}
