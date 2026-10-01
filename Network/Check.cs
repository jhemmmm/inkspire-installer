namespace Inkspire.Panel.Network;

public enum CheckLevel
{
    Ok,
    /// <summary>Worth knowing, nothing to do.</summary>
    Info,
    Warning,
    /// <summary>Setup cannot continue / the other PCs cannot connect until this is fixed.</summary>
    Blocker,
}

/// <summary>One line of the system check or the network diagnosis. <paramref name="FixId"/> names a one-click repair the
/// dialog may offer ("firewall", "unblock", "appurl").</summary>
public sealed record CheckResult(string Name, CheckLevel Level, string Detail, string? FixId = null);
