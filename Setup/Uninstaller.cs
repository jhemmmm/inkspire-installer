using System.Diagnostics;
using Inkspire.Panel.App;

namespace Inkspire.Panel.Setup;

/// <summary><c>InkspirePanel.exe --uninstall</c>: stops the services, removes the Run entry, shortcuts and firewall rule, and deletes
/// the program folders. The data folder (database, uploads, backups) is kept unless the user chooses to delete it too.</summary>
public static class Uninstaller
{
    public static int Run(AppServices ctx)
    {
        var root = ctx.Paths.InstallRoot;
        var answer = MessageBox.Show(
            $"Remove Inkspire from this PC?\n\nInstall folder: {root}\n\nYes = remove the programs but KEEP the database, uploads and backups (the data folder)\nNo = remove EVERYTHING, including the database and uploaded files\nCancel = do nothing",
            "Uninstall Inkspire", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
        if (answer == DialogResult.Cancel) return 0;
        var keepData = answer == DialogResult.Yes;
        if (!keepData)
        {
            var confirm = MessageBox.Show("This permanently deletes every job order, customer, payment record and uploaded file. Are you sure?",
                "Uninstall Inkspire", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return 0;
        }

        try
        {
            ctx.Services.StopAllAsync().GetAwaiter().GetResult();
            ctx.Services.SweepOrphans();
            ctx.Services.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Stop during uninstall: " + ex.Message);
        }

        StartupRegistration.Apply(false, ctx.Paths);
        Shortcuts.Delete(Shortcuts.StartMenuLink);
        Shortcuts.Delete(Shortcuts.DesktopLink);
        InstallLocator.DeletePointer();

        var code = Elevation.RunAdminTasksAsync(new[] { new AdminTaskRequest("firewall-remove", Array.Empty<string>()) }, ctx.Paths.LogsDir, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (code == Elevation.ExitCancelled) Log.Warn("The firewall rule was left in place (administrator prompt declined).");

        // Everything but data\ and panel\ (this exe is running from panel\ and cannot delete itself).
        foreach (var dir in new[]
                 {
                     ctx.Paths.PhpDir, ctx.Paths.NginxDir, ctx.Paths.AppDir, ctx.Paths.AppOldDir, ctx.Paths.AppNewDir,
                     ctx.Paths.InstallRoot + Path.DirectorySeparatorChar + "app.failed", ctx.Paths.DownloadsDir, ctx.Paths.ConfigDir, ctx.Paths.LogsDir,
                 })
        {
            try { FolderOps.DeleteTree(dir); }
            catch (Exception ex) { Log.Warn($"Could not delete {dir}: {ex.Message}"); }
        }
        if (!keepData)
        {
            try { FolderOps.DeleteTree(ctx.Paths.DataDir); }
            catch (Exception ex) { Log.Warn($"Could not delete the data folder: {ex.Message}"); }
        }

        // The running exe cannot delete itself: hand the rest to cmd after we exit.
        var target = keepData ? ctx.Paths.PanelDir : root;
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{target}\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch
        {
            // the folder simply stays; harmless
        }

        MessageBox.Show(keepData
                ? $"Inkspire was removed. Your database, uploads and backups remain in {ctx.Paths.DataDir}."
                : "Inkspire was removed completely.",
            "Uninstall Inkspire", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}
