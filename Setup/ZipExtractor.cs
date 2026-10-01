using System.IO.Compression;

namespace Inkspire.Panel.Setup;

/// <summary>Extracts a zip into a folder: refuses entries that would land outside it ("zip-slip"), can drop a single common
/// top-level folder (nginx and the Inkspire bundle ship as <c>name\...</c>), reports progress, retries locked files.</summary>
public static class ZipExtractor
{
    public static void Extract(string zipPath, string destDir, bool stripTopLevel, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var root = Path.GetFullPath(destDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.ToList();
        var strip = stripTopLevel ? CommonRoot(entries.Select(e => e.FullName)) : null;

        var done = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (strip is not null)
            {
                if (name.Equals(strip, StringComparison.Ordinal) || name.Equals(strip + "/", StringComparison.Ordinal)) { done++; continue; }
                name = name[(strip.Length + 1)..];
            }
            if (name.Length == 0) { done++; continue; }

            var target = Path.GetFullPath(Path.Combine(destDir, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !target.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The archive contains an unsafe path: {entry.FullName}");

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ExtractFile(entry, target);
            }
            done++;
            if (done % 100 == 0 || done == entries.Count) progress?.Report((done, entries.Count));
        }
        progress?.Report((entries.Count, entries.Count));
    }

    /// <summary>The first path segment when EVERY entry sits under the same one (and it is a folder), else null.</summary>
    public static string? CommonRoot(IEnumerable<string> entryNames)
    {
        string? root = null;
        var any = false;
        foreach (var raw in entryNames)
        {
            var name = raw.Replace('\\', '/');
            var slash = name.IndexOf('/');
            if (slash < 0) return null; // a file at the top level: nothing to strip
            var first = name[..slash];
            if (root is null) root = first;
            else if (!string.Equals(root, first, StringComparison.Ordinal)) return null;
            any = true;
        }
        return any ? root : null;
    }

    private static void ExtractFile(ZipArchiveEntry entry, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                entry.ExtractToFile(target, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Thread.Sleep(200 * attempt); // antivirus scanning the file we just wrote
            }
        }
    }
}
