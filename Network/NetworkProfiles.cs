namespace Inkspire.Panel.Network;

/// <summary>A network Windows is connected to and how it classified it. "Public" is what Windows picks for Wi-Fi it does not
/// know; firewall rules limited to the Private profile would never apply to it, which is why the panel's rule covers all profiles.</summary>
public sealed record NetworkProfileInfo(string Name, string Category);

public static class NetworkProfiles
{
    /// <summary>Connected networks via the Windows Network List Manager. Empty when it cannot be read.</summary>
    public static IReadOnlyList<NetworkProfileInfo> Connected()
    {
        var result = new List<NetworkProfileInfo>();
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (type is null) return result;
            dynamic manager = Activator.CreateInstance(type)!;
            const int connected = 1; // NLM_ENUM_NETWORK_CONNECTED
            foreach (dynamic network in manager.GetNetworks(connected))
            {
                int category = network.GetCategory();
                result.Add(new NetworkProfileInfo((string)network.GetName(), category switch { 0 => "Public", 1 => "Private", 2 => "Domain", _ => "Unknown" }));
            }
        }
        catch
        {
            // informational only
        }
        return result;
    }
}
