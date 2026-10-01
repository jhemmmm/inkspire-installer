using Inkspire.Panel.Config;

namespace Inkspire.Panel.Network;

public sealed record FirewallRuleInfo(
    string Name, bool Enabled, string LocalPorts, string RemoteAddresses, int Profiles, int Action, int Direction, int Protocol, string? Application);

/// <summary>What Windows Firewall currently does about Inkspire's web port.</summary>
/// <param name="ServiceRunning">The Windows Defender Firewall service is running (a third-party firewall may have taken over).</param>
/// <param name="ActiveProfiles">Bit mask of the profiles in use now: 1 domain, 2 private, 4 public.</param>
/// <param name="WebRule">Our allow rule for the configured port, if present.</param>
/// <param name="BlockingRules">Enabled inbound block rules that apply to nginx.exe or the port: they beat any allow rule.</param>
public sealed record FirewallStatus(
    bool ServiceRunning, int ActiveProfiles, FirewallRuleInfo? WebRule, IReadOnlyList<FirewallRuleInfo> BlockingRules, string? Error)
{
    public static FirewallStatus Unknown(string error) => new(false, 0, null, Array.Empty<FirewallRuleInfo>(), error);
}

/// <summary>Windows Firewall through the HNetCfg COM API (typed values, no text parsing). Reading works as a normal user;
/// changing rules needs administrator rights and runs inside the elevated <c>--admin-task</c> helper.</summary>
public static class FirewallManager
{
    public const string Group = "Inkspire";
    private const int ProfilesAll = 0x7FFFFFFF;
    private const int DirectionIn = 1;
    private const int ActionBlock = 0;
    private const int ActionAllow = 1;
    private const int ProtocolTcp = 6;
    private const string PrivateRanges = "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16";

    public static string RuleName(int port) => $"Inkspire Web ({port})";

    public static string RemoteFor(FirewallScope scope) => scope switch
    {
        FirewallScope.Any => "*",
        FirewallScope.PrivateRanges => PrivateRanges,
        _ => "LocalSubnet",
    };

    // ---------------------------------------------------------------- reading (no admin)

    public static FirewallStatus Inspect(int port, string nginxExe, FirewallScope scope)
    {
        try
        {
            dynamic policy = Create("HNetCfg.FwPolicy2");
            var active = (int)policy.CurrentProfileTypes;
            var service = ServiceRunning();

            FirewallRuleInfo? web = null;
            var blocking = new List<FirewallRuleInfo>();
            foreach (dynamic raw in policy.Rules)
            {
                var info = Read(raw);
                if (info.Name == RuleName(port)) web = info;
                if (info.Enabled && info.Direction == DirectionIn && info.Action == ActionBlock && BlocksUs(info, port, nginxExe))
                    blocking.Add(info);
            }
            return new FirewallStatus(service, active, web, blocking, null);
        }
        catch (Exception ex)
        {
            return FirewallStatus.Unknown(ex.Message);
        }
    }

    /// <summary>True when <paramref name="rule"/> is the allow rule the current settings call for.</summary>
    public static bool RuleMatches(FirewallRuleInfo rule, int port, FirewallScope scope)
    {
        if (!rule.Enabled || rule.Action != ActionAllow || rule.Direction != DirectionIn) return false;
        if (rule.Protocol != ProtocolTcp && rule.Protocol != 256 /* any */) return false;
        if (!rule.LocalPorts.Split(',').Select(p => p.Trim()).Contains(port.ToString())) return false;
        if ((rule.Profiles & 7) != 7 && rule.Profiles != ProfilesAll) return false; // must cover domain+private+public
        return scope switch
        {
            FirewallScope.Any => rule.RemoteAddresses is "*" or "Any",
            FirewallScope.PrivateRanges => rule.RemoteAddresses.Contains("192.168.0.0", StringComparison.Ordinal),
            _ => rule.RemoteAddresses.Contains("LocalSubnet", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool BlocksUs(FirewallRuleInfo rule, int port, string nginxExe)
    {
        var program = !string.IsNullOrEmpty(rule.Application)
                      && string.Equals(Environment.ExpandEnvironmentVariables(rule.Application), nginxExe, StringComparison.OrdinalIgnoreCase);
        var ports = rule.LocalPorts.Split(',').Select(p => p.Trim()).Contains(port.ToString());
        return program || ports;
    }

    private static FirewallRuleInfo Read(dynamic r) => new(
        (string)(r.Name ?? ""),
        (bool)r.Enabled,
        (string)(r.LocalPorts ?? ""),
        (string)(r.RemoteAddresses ?? ""),
        (int)r.Profiles,
        (int)r.Action,
        (int)r.Direction,
        (int)r.Protocol,
        (string?)r.ApplicationName);

    /// <summary>Is the "Windows Defender Firewall" service (MpsSvc) running?</summary>
    private static bool ServiceRunning() => WindowsService.IsRunning("MpsSvc");

    // ---------------------------------------------------------------- changing (administrator)

    /// <summary>Adds (replacing any older one) the inbound allow rule for <paramref name="port"/>: TCP, every profile — a
    /// shop network is often labelled "Public" by Windows — limited to <paramref name="scope"/>.</summary>
    public static void AddWebRule(int port, FirewallScope scope)
    {
        dynamic policy = Create("HNetCfg.FwPolicy2");
        RemoveByName(policy, RuleName(port));

        dynamic rule = Create("HNetCfg.FWRule");
        rule.Name = RuleName(port);
        rule.Description = "Lets the other PCs on the shop network open Inkspire. Created by Inkspire Control Panel.";
        rule.Grouping = Group;
        rule.Protocol = ProtocolTcp;
        rule.LocalPorts = port.ToString();
        rule.Direction = DirectionIn;
        rule.Action = ActionAllow;
        rule.Profiles = ProfilesAll;
        rule.RemoteAddresses = RemoteFor(scope);
        rule.Enabled = true;
        policy.Rules.Add(rule);
    }

    /// <summary>Removes every rule the panel created (rule group "Inkspire"), whatever port it was for.</summary>
    public static int RemoveAllInkspireRules()
    {
        dynamic policy = Create("HNetCfg.FwPolicy2");
        var names = new List<string>();
        foreach (dynamic raw in policy.Rules)
        {
            if (((string?)raw.Grouping ?? "") == Group || ((string?)raw.Name ?? "").StartsWith("Inkspire Web (", StringComparison.Ordinal))
                names.Add((string)raw.Name);
        }
        foreach (var name in names.Distinct()) RemoveByName(policy, name);
        return names.Count;
    }

    /// <summary>Deletes the inbound block rules Windows creates when someone clicks "Cancel" on the "Allow nginx through the
    /// firewall?" prompt. A block rule always wins over an allow rule.</summary>
    public static int RemoveBlockingRules(int port, string nginxExe)
    {
        dynamic policy = Create("HNetCfg.FwPolicy2");
        var victims = new List<string>();
        foreach (dynamic raw in policy.Rules)
        {
            var info = Read(raw);
            if (info.Enabled && info.Direction == DirectionIn && info.Action == ActionBlock && BlocksUs(info, port, nginxExe))
                victims.Add(info.Name);
        }
        foreach (var name in victims.Distinct()) RemoveByName(policy, name);
        return victims.Count;
    }

    private static void RemoveByName(dynamic policy, string name)
    {
        // Rules.Remove deletes one rule per call; duplicates (an old install) need a loop.
        for (var i = 0; i < 20; i++)
        {
            var found = false;
            foreach (dynamic raw in policy.Rules)
            {
                if ((string?)raw.Name == name) { found = true; break; }
            }
            if (!found) return;
            policy.Rules.Remove(name);
        }
    }

    private static object Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException($"{progId} is not available on this Windows."))!;
}
