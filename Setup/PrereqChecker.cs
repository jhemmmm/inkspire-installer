using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Inkspire.Panel.Network;
using Microsoft.Win32;

namespace Inkspire.Panel.Setup;

/// <summary>Pre-flight checks shown on the wizard's "System check" page.</summary>
public static class PrereqChecker
{
    public static bool IsVcRedist2015PlusInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            if (key is null) return false;
            var installed = key.GetValue("Installed") as int? ?? 0;
            var major = key.GetValue("Major") as int? ?? 0;
            return installed == 1 && major >= 14;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> HasInternetAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(6000);
            using var r = await http.GetAsync("https://api.github.com/", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return (int)r.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<CheckResult> CheckSystem(WizardState s)
    {
        var list = new List<CheckResult>();

        list.Add(Environment.Is64BitOperatingSystem && RuntimeInformation.OSArchitecture == Architecture.X64
            ? new("64-bit Windows", CheckLevel.Ok, RuntimeInformation.OSDescription)
            : new("64-bit Windows", CheckLevel.Blocker, "PHP and nginx need 64-bit Windows on an x64 PC."));

        list.Add(Environment.OSVersion.Version >= new Version(10, 0)
            ? new("Windows 10 or newer", CheckLevel.Ok, Environment.OSVersion.VersionString)
            : new("Windows 10 or newer", CheckLevel.Blocker, "Windows 10 or 11 is required."));

        list.Add(PortCheck("Web port", s.WebPort, blocker: true));
        for (var i = 0; i < s.PoolSize; i++) list.Add(PortCheck("PHP worker port", s.PoolBasePort + i, blocker: true, loopbackOnly: true));

        list.Add(IsVcRedist2015PlusInstalled()
            ? new("Visual C++ runtime", CheckLevel.Ok, "installed")
            : new("Visual C++ runtime", CheckLevel.Info, "Missing: it will be installed (part of the one administrator prompt)."));

        list.Add(CheckFolder(s.InstallRoot));
        list.Add(CheckDisk(s.InstallRoot));

        var lan = LanAddresses.List();
        list.Add(lan.Count > 0
            ? new("Network", CheckLevel.Ok, string.Join(", ", lan.Select(a => $"{a.Ip} ({a.Adapter}{(a.IsDhcp ? ", DHCP" : "")})")))
            : new("Network", CheckLevel.Warning, "No network address found. The other PCs cannot reach this one until it is connected to the shop network."));

        list.Add(s.IsRepair
            ? new("Existing install", CheckLevel.Info, "Found one in this folder: its database and settings are kept; programs and configuration are repaired.")
            : new("Existing install", CheckLevel.Ok, "none"));
        return list;
    }

    private static CheckResult CheckFolder(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return new("Install folder", CheckLevel.Blocker, "Choose a folder.");
        string full;
        try
        {
            full = Path.GetFullPath(root);
        }
        catch (Exception ex)
        {
            return new("Install folder", CheckLevel.Blocker, ex.Message);
        }

        if (full.Any(c => c > 127))
            return new("Install folder", CheckLevel.Blocker, "nginx cannot handle special characters in its folder path. Use a plain folder such as C:\\Inkspire.");
        if (Path.GetPathRoot(full) is { } driveRoot && new DriveInfo(driveRoot).DriveType == DriveType.Network)
            return new("Install folder", CheckLevel.Blocker, "Install on a drive inside this PC, not a network drive: SQLite is not safe over a network share.");
        if (full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase))
            return new("Install folder", CheckLevel.Blocker, "Program Files is write-protected. Use a folder such as C:\\Inkspire.");
        if (full.Contains(' '))
            return new("Install folder", CheckLevel.Warning, "Spaces in the path work, but a short path like C:\\Inkspire is safer.");

        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, ".write-test-" + Guid.NewGuid().ToString("N")[..6]);
            File.WriteAllText(probe, "x");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            return new("Install folder", CheckLevel.Blocker, $"This PC user cannot write to {full}: {ex.Message}");
        }
        return new("Install folder", CheckLevel.Ok, full);
    }

    private static CheckResult CheckDisk(string root)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
            return freeGb >= 2
                ? new("Free disk space", CheckLevel.Ok, $"{freeGb:F1} GB free on {drive.Name}")
                : new("Free disk space", CheckLevel.Blocker, $"Only {freeGb:F1} GB free on {drive.Name}; at least 2 GB is needed (uploads and backups need more over time).");
        }
        catch
        {
            return new("Free disk space", CheckLevel.Warning, "Could not determine the free space.");
        }
    }

    private static CheckResult PortCheck(string name, int port, bool blocker, bool loopbackOnly = false)
    {
        var free = loopbackOnly ? PortProbe.IsFreeLoopback(port) : PortProbe.IsFree(port);
        if (free) return new($"{name} {port}", CheckLevel.Ok, "free");
        var owner = PortProbe.WhoHolds(port);
        var who = owner is null ? "another program" : $"{owner.ProcessName} (pid {owner.Pid})";
        return new($"{name} {port}", blocker ? CheckLevel.Blocker : CheckLevel.Warning, $"in use by {who}." + (owner?.Hint is { } h ? " " + h : ""));
    }
}
