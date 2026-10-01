using System.Diagnostics;
using Inkspire.Panel.App;
using Inkspire.Panel.Services;

namespace Inkspire.Panel.UI;

/// <summary>Small helpers shared by the forms.</summary>
public static class Ui
{
    public static readonly Color Green = Color.FromArgb(46, 160, 67);
    public static readonly Color Amber = Color.FromArgb(222, 150, 0);
    public static readonly Color Red = Color.FromArgb(200, 50, 50);
    public static readonly Color Grey = Color.FromArgb(140, 140, 140);

    public static Color StateColor(ServiceState state) => state switch
    {
        ServiceState.Running => Green,
        ServiceState.Failed => Red,
        ServiceState.Stopped => Grey,
        _ => Amber,
    };

    public static string StateText(ServiceStatus status) => status.State switch
    {
        ServiceState.Running => "Running",
        ServiceState.Starting => "Starting…",
        ServiceState.Degraded => "Degraded",
        ServiceState.WaitingRestart => status.NextRetryUtc is { } t
            ? $"Restarting in {Math.Max(0, (int)Math.Ceiling((t - DateTime.UtcNow).TotalSeconds))}s"
            : "Restarting…",
        ServiceState.Failed => "Failed",
        ServiceState.Stopping => "Stopping…",
        _ => "Stopped",
    };

    public static string FormatUptime(TimeSpan t) =>
        t.TotalHours >= 24 ? $"{(int)t.TotalDays}d {t.Hours}h" : $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    public static bool TryCopy(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(50); // clipboard briefly locked by another app
            }
        }
        return false;
    }

    /// <summary>Open a URL, file or folder with its default app.</summary>
    public static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {target}: {ex.Message}");
            MessageBox.Show($"Could not open:\n{target}\n\n{ex.Message}", "Inkspire Control Panel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static string BrowserUrl(AppServices ctx) => $"http://localhost:{ctx.Config.Web.Port}/";
}
