using Microsoft.Win32;

namespace Inkspire.Panel.App;

/// <summary>"Run at login": a per-user HKCU Run entry that starts the installed panel with <c>--autostart</c>.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InkspirePanel";

    /// <param name="valueName">Registry value name; tests use their own so they never touch a real install's entry.</param>
    public static bool IsEnabled(string valueName = ValueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(valueName) is string s && s.Length > 0;
    }

    public static string? CurrentCommand(string valueName = ValueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(valueName) as string;
    }

    public static void Apply(bool enabled, AppPaths paths, string valueName = ValueName)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(valueName, $"\"{paths.PanelExe}\" --autostart");
        else key.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
