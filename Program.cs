using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.UI.Wizard;

namespace Inkspire.Panel;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var cli = CommandLine.Parse(args);

        // Elevated helper mode: no UI, no single-instance lock, no config. Do the administrator work and exit.
        if (cli.AdminTasks.Count > 0)
        {
            return AdminTaskRunner.Run(cli);
        }

        // Headless developer harnesses.
        if (cli.DevProvisionRoot is not null)
        {
            return Setup.DevProvision.Run(cli.DevProvisionRoot, cli.DevAdminEmail);
        }
        if (cli.DevInstallRoot is not null)
        {
            return Setup.DevInstall.Run(cli);
        }
        if (cli.DevSmokeRoot is not null)
        {
            return Setup.DevSmoke.Run(cli.DevSmokeRoot, cli.DevSmokeHoldSeconds);
        }
        if (cli.DevFirewallPort is { } fwPort)
        {
            return Setup.DevFirewall.Run(fwPort, cli.DevFirewallScope, cli.DevKeep);
        }
        if (cli.DevUpdateRoot is not null && cli.DevUpdateZip is not null)
        {
            return Setup.DevUpdate.Run(cli.DevUpdateRoot, cli.DevUpdateZip, cli.DevUpdateVersion ?? "v0.0.1-dev");
        }
        if (cli.DevScreenshotDir is not null)
        {
            return UI.DevScreenshots.Run(cli.DevScreenshotDir, cli.InstallRootOverride);
        }

        ApplicationConfiguration.Initialize();
        GlobalExceptionHandler.Install();

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Another panel is running: ask it to show itself and quit.
            SingleInstance.SignalShow();
            return 0;
        }

        var installRoot = cli.Wizard ? null : cli.InstallRootOverride ?? InstallLocator.Find();
        if (installRoot is null)
        {
            // First run (or an explicit --wizard): the setup wizard.
            Application.Run(new WizardForm(cli, instance));
            return 0;
        }

        var paths = new AppPaths(installRoot);
        var store = new ConfigStore(paths.PanelConfigFile);
        PanelConfig config;
        try
        {
            config = store.Load();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load panel.json", ex);
            MessageBox.Show($"The panel configuration could not be read:\n\n{ex.Message}\n\nRun the setup wizard again to repair it (InkspirePanel.exe --wizard).",
                "Inkspire Control Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        AppServices.Initialize(paths, store, config);
        Log.Info($"Panel starting. Install root: {installRoot}. Autostart={cli.Autostart}");

        if (cli.Uninstall)
        {
            return Setup.Uninstaller.Run(AppServices.Current);
        }

        Application.Run(new PanelAppContext(cli, instance));
        return 0;
    }
}
