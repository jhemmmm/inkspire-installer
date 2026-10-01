using System.Drawing.Imaging;
using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.UI.Panel;
using Inkspire.Panel.UI.Wizard;

namespace Inkspire.Panel.UI;

/// <summary><c>InkspirePanel.exe --dev-screenshots &lt;dir&gt; [--install-root &lt;root&gt;]</c>: renders every wizard page and, when an install
/// root is given, the main window and the Network &amp; clients dialog to PNG files, so layouts can be reviewed without
/// clicking through them. The forms are shown far off-screen and never receive input.</summary>
public static class DevScreenshots
{
    public static int Run(string outDir, string? installRoot)
    {
        Directory.CreateDirectory(outDir);
        ApplicationConfiguration.Initialize();
        try
        {
            var cli = CommandLine.Parse(new[] { "--wizard" });
            using (var wizard = new WizardForm(cli, null))
            {
                Prepare(wizard);
                for (var i = 0; i < wizard.PageCount; i++)
                {
                    wizard.DevShowPage(i);
                    Settle();
                    Capture(wizard, Path.Combine(outDir, $"wizard-{i + 1:00}.png"));
                }
            }

            if (installRoot is not null)
            {
                var paths = new AppPaths(installRoot);
                var store = new ConfigStore(paths.PanelConfigFile);
                AppServices.Initialize(paths, store, store.Load());
                var app = new PanelAppContext(CommandLine.Parse(new[] { "--show" }), SingleInstance.TryAcquire()!);
                // PanelAppContext opened the main window itself (and, with --show, started the services).
                Pump(TimeSpan.FromSeconds(2.5));
                if (PanelAppContext.Current is { } current)
                {
                    current.ShowMain();
                    Settle();
                    foreach (Form f in Application.OpenForms)
                    {
                        if (f is MainForm main)
                        {
                            Prepare(main);
                            Settle();
                            Capture(main, Path.Combine(outDir, "panel-main.png"));
                        }
                    }
                }
                using var net = new NetworkClientsForm(AppServices.Current);
                Prepare(net);
                Pump(TimeSpan.FromSeconds(4)); // the diagnosis runs in the background and posts its result to the UI thread
                Capture(net, Path.Combine(outDir, "panel-network.png"));
                app.ExitAsync().GetAwaiter().GetResult();
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("dev-screenshots failed", ex);
            File.WriteAllText(Path.Combine(outDir, "error.txt"), ex.ToString());
            return 1;
        }
    }

    private static void Prepare(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-5000, 0);
        f.ShowInTaskbar = false;
        if (!f.Visible) f.Show();
    }

    /// <summary>Keep the message loop alive for a while (Thread.Sleep would starve the posted callbacks).</summary>
    private static void Pump(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(30);
        }
    }

    private static void Settle()
    {
        for (var i = 0; i < 12; i++)
        {
            Application.DoEvents();
            Thread.Sleep(40);
        }
    }

    private static void Capture(Form f, string path)
    {
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(path, ImageFormat.Png);
    }
}
