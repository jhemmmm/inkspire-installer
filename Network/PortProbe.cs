using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Inkspire.Panel.Network;

/// <summary>Who is listening on a TCP port: the process, and a plain-language hint for the usual suspects.</summary>
public sealed record PortOwner(int Pid, string ProcessName, string? Path, string? Hint);

public static class PortProbe
{
    /// <summary>True when nothing is listening on the port on any interface (the same bind nginx performs).</summary>
    public static bool IsFree(int port) => CanBind(IPAddress.Any, port);

    /// <summary>Same for 127.0.0.1 only (the php-cgi workers).</summary>
    public static bool IsFreeLoopback(int port) => CanBind(IPAddress.Loopback, port);

    private static bool CanBind(IPAddress address, int port)
    {
        try
        {
            var listener = new TcpListener(address, port) { ExclusiveAddressUse = true };
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>True when some process is listening on <paramref name="port"/> on a non-loopback address
    /// (so other PCs could reach it). Used by the network diagnostics.</summary>
    public static bool IsListeningOnAllInterfaces(int port)
    {
        foreach (var row in ListenerRows())
        {
            if (row.Port == port && row.Address == 0) return true; // 0.0.0.0
        }
        return false;
    }

    public static PortOwner? WhoHolds(int port)
    {
        foreach (var row in ListenerRows())
        {
            if (row.Port != port) continue;
            return Describe(row.Pid);
        }
        return null;
    }

    private static PortOwner Describe(int pid)
    {
        string name;
        try { name = Process.GetProcessById(pid).ProcessName; }
        catch { name = "unknown"; }
        var path = Processes.ProcessSweep.GetImagePath(pid);

        string? hint = null;
        if (pid == 4) hint = "That is Windows' own HTTP service (IIS / World Wide Web Publishing / another app using http.sys). Stop that service or pick another port.";
        else if (name.Equals("httpd", StringComparison.OrdinalIgnoreCase)) hint = "That looks like Apache (XAMPP / Laragon). Stop it or pick another port.";
        else if (name.Equals("nginx", StringComparison.OrdinalIgnoreCase)) hint = "Another nginx is running (Laragon / a previous install?). Stop it or pick another port.";
        else if (name.Contains("skype", StringComparison.OrdinalIgnoreCase)) hint = "Skype can take ports 80/443; pick another port.";
        return new PortOwner(pid, name, path, hint);
    }

    private readonly record struct ListenerRow(uint Address, int Port, int Pid);

    private static IEnumerable<ListenerRow> ListenerRows()
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (size <= 0) yield break;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (result != 0) yield break;
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            var rows = new List<ListenerRow>(count);
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buffer + 4 + i * rowSize);
                var port = (ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort);
                rows.Add(new ListenerRow(row.LocalAddr, port, (int)row.OwningPid));
            }
            foreach (var r in rows) yield return r;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sort, int af, int tableClass, uint reserved);
}
