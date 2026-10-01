using System.Text.Json;

namespace Inkspire.Panel.App;

/// <summary>Finds the install root: next to the running exe (<c>&lt;root&gt;\panel\InkspirePanel.exe</c>), else a pointer
/// file in %LOCALAPPDATA%. Null means "not installed yet" and the wizard runs.</summary>
public static class InstallLocator
{
    private sealed record Pointer(string InstallRoot);

    /// <summary>Tests point this at a temp file so they never touch the pointer of a real install.</summary>
    public static string? PointerFileOverride { get; set; }

    public static string PointerFile => PointerFileOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inkspire", "install.json");

    public static string? Find()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null && string.Equals(Path.GetFileName(exeDir), "panel", StringComparison.OrdinalIgnoreCase))
        {
            var root = Directory.GetParent(exeDir)?.FullName;
            if (root is not null && HasConfig(root)) return root;
        }

        try
        {
            if (File.Exists(PointerFile))
            {
                var p = JsonSerializer.Deserialize<Pointer>(File.ReadAllText(PointerFile));
                if (p is not null && HasConfig(p.InstallRoot)) return p.InstallRoot;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read install pointer: {ex.Message}");
        }
        return null;
    }

    public static void WritePointer(string installRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PointerFile)!);
        File.WriteAllText(PointerFile, JsonSerializer.Serialize(new Pointer(installRoot)));
    }

    public static void DeletePointer()
    {
        try { if (File.Exists(PointerFile)) File.Delete(PointerFile); } catch { }
    }

    private static bool HasConfig(string root) => File.Exists(Path.Combine(root, "config", "panel.json"));
}
