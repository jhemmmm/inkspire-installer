using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Inkspire.Panel.Network;

/// <summary>One IPv4 address other PCs on the network can use to reach this one.</summary>
/// <param name="Cidr">Subnet in CIDR form, e.g. 192.168.1.0/24 (what the firewall's LocalSubnet means for this adapter).</param>
public sealed record LanAddress(string Ip, string Adapter, bool IsDhcp, bool HasGateway, string Cidr, NetworkInterfaceType Type);

public static class LanAddresses
{
    private static readonly string[] IgnoredAdapters =
    {
        "vethernet", "wsl", "docker", "virtualbox", "vmware", "hyper-v", "vmnet", "bluetooth", "loopback", "pseudo", "tap-", "tailscale", "zerotier",
    };

    /// <summary>Usable IPv4 addresses, best first: adapters with a default gateway, wired before wireless.</summary>
    public static IReadOnlyList<LanAddress> List()
    {
        var result = new List<LanAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var label = (nic.Name + " " + nic.Description).ToLowerInvariant();
                if (IgnoredAdapters.Any(label.Contains)) continue;

                var props = nic.GetIPProperties();
                IPv4InterfaceProperties? v4 = null;
                try { v4 = props.GetIPv4Properties(); } catch { /* adapter without IPv4 */ }
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

                foreach (var unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = unicast.Address.ToString();
                    if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue; // link-local: DHCP failed
                    result.Add(new LanAddress(ip, nic.Name, v4?.IsDhcpEnabled ?? false, hasGateway,
                        Cidr(unicast.Address, unicast.IPv4Mask), nic.NetworkInterfaceType));
                }
            }
        }
        catch
        {
            // return what we have
        }
        return result
            .OrderByDescending(a => a.HasGateway)
            .ThenBy(a => a.Type == NetworkInterfaceType.Ethernet ? 0 : 1)
            .ToList();
    }

    public static LanAddress? Primary() => List().FirstOrDefault();

    private static string Cidr(IPAddress address, IPAddress? mask)
    {
        if (mask is null) return address + "/?";
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var net = new byte[4];
        var prefix = 0;
        for (var i = 0; i < 4; i++)
        {
            net[i] = (byte)(a[i] & m[i]);
            for (var bit = 7; bit >= 0; bit--) if ((m[i] & (1 << bit)) != 0) prefix++;
        }
        return $"{new IPAddress(net)}/{prefix}";
    }
}
