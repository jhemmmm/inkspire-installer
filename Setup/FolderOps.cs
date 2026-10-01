namespace Inkspire.Panel.Setup;

/// <summary>File-system helpers that are safe in a tree containing junctions. <c>Directory.Delete(path, recursive: true)</c>
/// throws "Access denied" when it meets a junction (it treats it as a volume mount point), so every place that removes an
/// app folder (update clean-up, uninstall, repair) must go through <see cref="DeleteTree"/> instead.</summary>
public static class FolderOps
{
    /// <summary>Deletes a folder tree. Junctions/symlinks inside are removed as links: what they point to is never touched.</summary>
    public static void DeleteTree(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists) return;

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Retry(() => Directory.Delete(path, recursive: false));
            return;
        }

        foreach (var dir in info.EnumerateDirectories()) DeleteTree(dir.FullName);
        foreach (var file in info.EnumerateFiles())
        {
            Retry(() =>
            {
                if (file.IsReadOnly) file.IsReadOnly = false;
                file.Delete();
            });
        }
        Retry(() => Directory.Delete(path, recursive: false));
    }

    /// <summary>Moves every entry of <paramref name="from"/> into <paramref name="to"/> (created if needed), one by one, with a
    /// short retry for files an antivirus scanner still holds. Works when the folder itself is held open (an Explorer window
    /// in it is enough to make a plain rename fail). Junctions move as links.</summary>
    public static void MoveContents(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(entry));
            Retry(() =>
            {
                if (Directory.Exists(entry)) Directory.Move(entry, dest);
                else File.Move(entry, dest, overwrite: true);
            });
        }
    }

    /// <summary><c>current</c> → <c>stash</c>, then <c>incoming</c> → <c>current</c>. When either half fails (a file in the folder is
    /// held open by an Explorer window, an editor, an antivirus scan) the original layout is restored and the error says so, so a
    /// failed swap never leaves the install half-moved.</summary>
    public static void SwapFolders(string current, string incoming, string stash, string what)
    {
        if (Directory.Exists(stash)) DeleteTree(stash);
        try
        {
            MoveContents(current, stash);
        }
        catch (Exception ex)
        {
            MoveContents(stash, current);
            throw new InvalidOperationException(InUseMessage(what, ex), ex);
        }
        try
        {
            MoveContents(incoming, current);
            Directory.Delete(incoming);
        }
        catch (Exception ex)
        {
            MoveContents(current, incoming);
            MoveContents(stash, current);
            throw new InvalidOperationException(InUseMessage(what, ex), ex);
        }
    }

    public static string InUseMessage(string what, Exception ex) =>
        $"A file or folder in the {what} folder is in use by another program (an open Explorer window, editor or terminal in that folder is enough). "
        + "Close it and try again; the current version was left unchanged. Detail: " + ex.Message;

    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 8)
            {
                Thread.Sleep(250 * attempt);
            }
        }
    }
}
