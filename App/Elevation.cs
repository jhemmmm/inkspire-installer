using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Inkspire.Panel.App;

/// <summary>Runs administrator-only work by relaunching this exe elevated in <c>--admin-task</c> mode and waiting. The panel
/// itself never runs elevated: its children (nginx, php) would inherit the rights.</summary>
public static class Elevation
{
    /// <summary>ERROR_CANCELLED: the user declined the UAC prompt.</summary>
    public const int ExitCancelled = 1223;

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>All tasks run in ONE elevated process (one UAC prompt). Returns its exit code, or <see cref="ExitCancelled"/>
    /// when the prompt was declined.</summary>
    public static async Task<int> RunAdminTasksAsync(IEnumerable<AdminTaskRequest> tasks, string logsDir, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var t in tasks)
        {
            psi.ArgumentList.Add("--admin-task");
            psi.ArgumentList.Add(t.Name);
            foreach (var a in t.Args) psi.ArgumentList.Add(a);
        }
        psi.ArgumentList.Add("--log-dir");
        psi.ArgumentList.Add(logsDir);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("The elevated helper did not start.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ExitCancelled)
        {
            return ExitCancelled;
        }

        using (process)
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
