using Inkspire.Panel.App;

namespace Inkspire.Panel.Setup;

/// <summary>Start-menu (and optional desktop) shortcuts through the WScript.Shell COM object.</summary>
public static class Shortcuts
{
    private const string Name = "Inkspire Control Panel.lnk";

    public static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name);

    public static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name);

    public static void Create(string linkPath, AppPaths paths)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(linkPath);
        shortcut.TargetPath = paths.PanelExe;
        shortcut.Arguments = "--show";
        shortcut.WorkingDirectory = paths.PanelDir;
        shortcut.IconLocation = paths.PanelExe + ",0";
        shortcut.Description = "Start, stop and check Inkspire on this PC";
        shortcut.Save();
    }

    public static void Delete(string linkPath)
    {
        try { if (File.Exists(linkPath)) File.Delete(linkPath); }
        catch (Exception ex) { Log.Warn($"Could not delete {linkPath}: {ex.Message}"); }
    }
}
