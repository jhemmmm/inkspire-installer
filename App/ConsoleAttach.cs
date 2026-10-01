using System.Runtime.InteropServices;

namespace Inkspire.Panel.App;

/// <summary>The panel is a WinExe (no console). The headless developer switches attach to the console that launched them so
/// <see cref="Console"/> output shows up in the terminal. Call before the first use of <see cref="Console"/>.</summary>
public static class ConsoleAttach
{
    private const int ATTACH_PARENT_PROCESS = -1;

    public static void Attach()
    {
        if (AttachConsole(ATTACH_PARENT_PROCESS))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
