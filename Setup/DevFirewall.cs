using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Network;

namespace Inkspire.Panel.Setup;

/// <summary><c>InkspirePanel.exe --dev-firewall &lt;port&gt; [LocalSubnet|PrivateRanges|Any] [--keep]</c>: the real firewall path end to end.
/// Reads the firewall as a normal user, raises the UAC prompt, adds the rule through the elevated helper, reads it back, then
/// removes it again (unless <c>--keep</c>, which leaves it so another PC can try to connect). Exit code 0 = every step behaved.</summary>
public static class DevFirewall
{
    public static int Run(int port, string? scopeName, bool keep)
    {
        ConsoleAttach.Attach();
        var scope = Enum.TryParse<FirewallScope>(scopeName, true, out var s) ? s : FirewallScope.LocalSubnet;
        var logs = Path.Combine(Path.GetTempPath(), "inkspire-firewall-test");
        Directory.CreateDirectory(logs);
        var fakeNginx = Path.Combine(logs, "nginx.exe");
        var failures = new List<string>();
        void Say(string line) => Console.WriteLine(line);

        void Show(string title)
        {
            var st = FirewallManager.Inspect(port, fakeNginx, scope);
            Say($"{title}: service running={st.ServiceRunning}, active profiles={st.ActiveProfiles}, " +
                (st.WebRule is null ? "no rule" : $"rule '{st.WebRule.Name}' enabled={st.WebRule.Enabled} ports={st.WebRule.LocalPorts} remote={st.WebRule.RemoteAddresses} profiles={st.WebRule.Profiles}"));
            if (st.Error is not null) failures.Add("inspect error: " + st.Error);
        }

        Show("before ");
        Say("Asking Windows for administrator approval to ADD the rule (approve the prompt)...");
        var code = Elevation.RunAdminTasksAsync(new[] { new AdminTaskRequest("firewall-add", new[] { port.ToString(), scope.ToString() }) }, logs, CancellationToken.None)
            .GetAwaiter().GetResult();
        Say($"elevated helper exit code: {code} (1223 = declined, 0 = ok)");
        if (code != 0) failures.Add($"add failed with {code}");

        var after = FirewallManager.Inspect(port, fakeNginx, scope);
        Show("after add");
        if (after.WebRule is null || !FirewallManager.RuleMatches(after.WebRule, port, scope)) failures.Add("the rule is missing or does not match after adding it");

        if (!keep)
        {
            Say("Asking for administrator approval to REMOVE the rule again...");
            code = Elevation.RunAdminTasksAsync(new[] { new AdminTaskRequest("firewall-remove", Array.Empty<string>()) }, logs, CancellationToken.None)
                .GetAwaiter().GetResult();
            Say($"elevated helper exit code: {code}");
            Show("after remove");
            if (FirewallManager.Inspect(port, fakeNginx, scope).WebRule is not null) failures.Add("the rule is still there after removing it");
        }
        else
        {
            Say($"Rule kept. Remove it later with: InkspirePanel.exe --dev-firewall {port} (without --keep) or the panel's Uninstall.");
        }

        Say($"helper log: {Path.Combine(logs, "admin-task.log")}");
        Say(failures.Count == 0 ? "FIREWALL OK" : "FIREWALL FAILED:\n  " + string.Join("\n  ", failures));
        return failures.Count == 0 ? 0 : 1;
    }
}
