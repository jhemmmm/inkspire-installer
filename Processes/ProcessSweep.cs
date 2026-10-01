using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Inkspire.Panel.Processes;

/// <summary>Kills nginx/php left behind by a previous panel that crashed. Matches the FULL image path under the install
/// root, never the image name alone: a dev machine may run Laragon's nginx or another XAMPP at the same time.</summary>
public static class ProcessSweep
{
    private static readonly string[] ImageNames = { "nginx", "php-cgi", "php" };

    public static int Sweep(string installRoot)
    {
        var prefix = installRoot.TrimEnd('\\', '/') + "\\";
        var killed = 0;
        foreach (var name in ImageNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = GetImagePath(p.Id);
                    if (path is null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill(entireProcessTree: true);
                    killed++;
                }
                catch
                {
                    // access denied or already gone: not ours to worry about
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        return killed;
    }

    /// <summary>Full path of a process image, or null when it cannot be queried (other user, protected process).</summary>
    public static string? GetImagePath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder text, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
