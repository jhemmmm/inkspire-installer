using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.Setup;

/// <summary><c>InkspirePanel.exe --dev-update &lt;root&gt; &lt;bundle.zip&gt; [version]</c>: the real <see cref="AppUpdater"/> against a scratch
/// install, without the UI. Exit code 0 = updated, 1 = failed (and rolled back when the failure was after the swap).</summary>
public static class DevUpdate
{
    public static int Run(string root, string zip, string version)
    {
        ConsoleAttach.Attach();
        var paths = new AppPaths(root);
        Log.Configure(paths.LogsDir);

        void Say(string line)
        {
            Console.WriteLine(line);
            Log.Info(line);
        }

        try
        {
            var store = new ConfigStore(paths.PanelConfigFile);
            var ctx = AppServices.Initialize(paths, store, store.Load());
            new AppUpdater(ctx).ApplyAsync(Path.GetFullPath(zip), version, Say, CancellationToken.None).GetAwaiter().GetResult();
            Say("UPDATE DONE");
            return 0;
        }
        catch (Exception ex)
        {
            Say("UPDATE FAILED: " + ex.Message);
            Log.Error("dev-update failed", ex);
            return 1;
        }
    }
}
