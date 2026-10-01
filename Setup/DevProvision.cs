using Inkspire.Panel.App;
using Inkspire.Panel.Config;

namespace Inkspire.Panel.Setup;

/// <summary><c>InkspirePanel.exe --dev-provision &lt;root&gt; [--admin-email you@example.com]</c>: headless provisioning of an
/// already-extracted tree (<c>&lt;root&gt;\app</c>, <c>php</c>, <c>nginx</c> present). The admin password comes from the
/// environment variable INKSPIRE_DEV_ADMIN_PASSWORD. Exit code 0 = success.</summary>
public static class DevProvision
{
    public static int Run(string root, string? adminEmail)
    {
        ConsoleAttach.Attach();
        var paths = new AppPaths(root);
        Directory.CreateDirectory(paths.LogsDir);
        Log.Configure(paths.LogsDir);

        void Say(string line)
        {
            Console.WriteLine(line);
            Log.Info(line);
        }

        try
        {
            var store = new ConfigStore(paths.PanelConfigFile);
            var config = store.Exists ? store.Load() : new PanelConfig { InstallRoot = paths.InstallRoot, InstalledAtUtc = DateTime.UtcNow };
            var provisioner = new Provisioner(paths, config, Say);

            provisioner.EnsureDirectories();
            provisioner.EnsureJunctions();
            provisioner.WriteRuntimeConfigs();
            provisioner.WriteEnv();
            provisioner.EnsureSqliteAsync(CancellationToken.None).GetAwaiter().GetResult();

            AdminAccount? admin = null;
            if (!string.IsNullOrWhiteSpace(adminEmail))
            {
                var password = Environment.GetEnvironmentVariable("INKSPIRE_DEV_ADMIN_PASSWORD") ?? "Dev-Password-2026!";
                admin = new AdminAccount("Dev Admin", adminEmail, password);
            }
            provisioner.BootstrapLaravelAsync(admin, CancellationToken.None).GetAwaiter().GetResult();

            store.Save(config);
            Say("PROVISION DONE");
            return 0;
        }
        catch (Exception ex)
        {
            Say("PROVISION FAILED: " + ex.Message);
            Log.Error("dev-provision failed", ex);
            return 1;
        }
    }
}
