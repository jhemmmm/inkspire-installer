using System.Diagnostics;

namespace Inkspire.Panel.Setup;

/// <summary>NTFS junctions (directory links). Unlike symlinks they need no administrator rights, which is why the panel
/// uses them for <c>app\storage</c> and <c>app\public\storage</c> instead of <c>artisan storage:link</c>.</summary>
public static class Junction
{
    public static bool IsLink(string path)
    {
        var di = new DirectoryInfo(path);
        return di.Exists && di.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    /// <summary>Where the link points (without the \\?\ or \??\ prefix), or null if it is not a link.</summary>
    public static string? TargetOf(string path)
    {
        if (!IsLink(path)) return null;
        var target = new DirectoryInfo(path).LinkTarget;
        return target is null ? null : Normalize(target);
    }

    public static bool PointsTo(string path, string target) =>
        TargetOf(path) is { } t && string.Equals(t, Normalize(target), StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes the link only; the folder it points to is untouched.</summary>
    public static void RemoveLink(string path)
    {
        if (IsLink(path)) Directory.Delete(path, recursive: false);
    }

    public static void Create(string link, string target)
    {
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        RemoveLink(link);

        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("/d");
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink");
        psi.ArgumentList.Add("/J");
        psi.ArgumentList.Add(link);
        psi.ArgumentList.Add(target);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("cmd.exe did not start.");
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 || !IsLink(link))
            throw new InvalidOperationException($"Could not create the folder link {link} -> {target}: {output.Trim()}");
    }

    private static string Normalize(string path)
    {
        foreach (var prefix in new[] { @"\??\", @"\\?\" })
            if (path.StartsWith(prefix, StringComparison.Ordinal)) path = path[prefix.Length..];
        return Path.GetFullPath(path).TrimEnd('\\', '/');
    }
}
