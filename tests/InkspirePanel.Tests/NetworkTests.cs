using System.Net;
using System.Net.Sockets;
using Inkspire.Panel.Network;
using Xunit;

namespace InkspirePanel.Tests;

public class NetworkTests
{
    private static (TcpListener Listener, int Port) Listen(IPAddress address)
    {
        var l = new TcpListener(address, 0);
        l.Start();
        return (l, ((IPEndPoint)l.LocalEndpoint).Port);
    }

    [Fact]
    public void A_port_with_a_listener_is_not_free_and_its_owner_is_found()
    {
        var (l, port) = Listen(IPAddress.Any);
        try
        {
            Assert.False(PortProbe.IsFree(port));
            Assert.Equal(Environment.ProcessId, PortProbe.WhoHolds(port)?.Pid);
            Assert.True(PortProbe.IsListeningOnAllInterfaces(port));
        }
        finally
        {
            l.Stop();
        }
        Assert.True(PortProbe.IsFree(port));
    }

    [Fact]
    public void A_loopback_only_listener_is_not_reachable_from_other_pcs()
    {
        var (l, port) = Listen(IPAddress.Loopback);
        try
        {
            Assert.False(PortProbe.IsFreeLoopback(port));
            Assert.False(PortProbe.IsListeningOnAllInterfaces(port)); // this is why php-cgi needs no firewall rule
        }
        finally
        {
            l.Stop();
        }
    }

    [Fact]
    public void Lan_address_listing_never_throws_and_skips_link_local_addresses()
    {
        var all = LanAddresses.List();
        Assert.All(all, a =>
        {
            Assert.DoesNotContain("169.254.", a.Ip);
            Assert.Matches(@"^\d+\.\d+\.\d+\.\d+/\d+$", a.Cidr);
        });
    }
}

public class FirewallReadTests
{
    [Fact]
    public void Inspect_reads_the_firewall_without_administrator_rights()
    {
        var status = Inkspire.Panel.Network.FirewallManager.Inspect(18080, @"C:\definitely-not-here\nginx.exe", Inkspire.Panel.Config.FirewallScope.LocalSubnet);

        Assert.Null(status.Error);          // the COM policy object opened and its rules enumerated without administrator rights
        Assert.Null(status.WebRule);        // nothing named "Inkspire Web (18080)" exists
        // The service state and the active profile depend on the machine (a CI runner may differ from a desktop): read, not asserted.
        Assert.Equal(Inkspire.Panel.Network.WindowsService.IsRunning("MpsSvc"), status.ServiceRunning);
    }

    [Theory]
    [InlineData(Inkspire.Panel.Config.FirewallScope.LocalSubnet, "LocalSubnet")]
    [InlineData(Inkspire.Panel.Config.FirewallScope.Any, "*")]
    public void Scope_maps_to_the_remote_address_the_rule_uses(Inkspire.Panel.Config.FirewallScope scope, string remote)
    {
        Assert.Equal(remote, Inkspire.Panel.Network.FirewallManager.RemoteFor(scope));
        Assert.Contains("192.168.0.0/16", Inkspire.Panel.Network.FirewallManager.RemoteFor(Inkspire.Panel.Config.FirewallScope.PrivateRanges));
    }

    [Fact]
    public void A_matching_rule_is_recognised_and_near_misses_are_not()
    {
        var good = new Inkspire.Panel.Network.FirewallRuleInfo("Inkspire Web (8080)", true, "8080", "LocalSubnet", 0x7FFFFFFF, 1, 1, 6, null);
        var scope = Inkspire.Panel.Config.FirewallScope.LocalSubnet;

        Assert.True(Inkspire.Panel.Network.FirewallManager.RuleMatches(good, 8080, scope));
        Assert.False(Inkspire.Panel.Network.FirewallManager.RuleMatches(good with { Enabled = false }, 8080, scope));
        Assert.False(Inkspire.Panel.Network.FirewallManager.RuleMatches(good with { LocalPorts = "8081" }, 8080, scope));
        Assert.False(Inkspire.Panel.Network.FirewallManager.RuleMatches(good with { Action = 0 }, 8080, scope));
        Assert.False(Inkspire.Panel.Network.FirewallManager.RuleMatches(good with { Profiles = 2 }, 8080, scope)); // private only: Public would be locked out
        Assert.False(Inkspire.Panel.Network.FirewallManager.RuleMatches(good with { RemoteAddresses = "*" }, 8080, scope));
        Assert.True(Inkspire.Panel.Network.FirewallManager.RuleMatches(good with { RemoteAddresses = "*" }, 8080, Inkspire.Panel.Config.FirewallScope.Any));
    }
}
