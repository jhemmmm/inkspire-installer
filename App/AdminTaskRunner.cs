using System.Diagnostics;
using System.Text;
using Inkspire.Panel.Config;
using Inkspire.Panel.Network;

namespace Inkspire.Panel.App;

/// <summary>Executes <c>--admin-task</c> requests inside the elevated helper instance. Tasks:
/// <list type="bullet">
/// <item><c>firewall-add &lt;port&gt; &lt;scope&gt;</c>: inbound TCP allow rule "Inkspire Web (port)"; scope = LocalSubnet | PrivateRanges | Any</item>
/// <item><c>firewall-unblock &lt;port&gt; &lt;nginx.exe&gt;</c>: delete block rules that would override it</item>
/// <item><c>firewall-remove</c>: delete every rule the panel created</item>
/// <item><c>vcredist &lt;exe&gt;</c>: run the Visual C++ runtime installer silently</item>
/// <item><c>acl-secure &lt;folder&gt; &lt;user-sid&gt;</c>: only that user, SYSTEM and Administrators may use the install folder (.env holds secrets)</item>
/// </list>
/// Exit code = first failing task's code, else 0. Logs to <c>&lt;--log-dir&gt;\admin-task.log</c>.</summary>
/// <summary>Which kinds of elevated task failed. Used as the helper's exit code.</summary>
[Flags]
public enum AdminTaskResult
{
    None = 0,
    Acl = 1,
    VcRedist = 2,
    Firewall = 4,
    Other = 8,
    NotElevated = 128,
}

public static class AdminTaskRunner
{
    public static int Run(CommandLine cli)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.AppendLine($"{DateTime.Now:HH:mm:ss} {s}");

        var exit = AdminTaskResult.None;
        try
        {
            if (!Elevation.IsElevated())
            {
                L("Not elevated; refusing to run admin tasks.");
                exit = AdminTaskResult.NotElevated;
            }
            else
            {
                foreach (var task in cli.AdminTasks)
                {
                    L($"== {task.Name} {string.Join(' ', task.Args)}");
                    bool ok;
                    try
                    {
                        ok = RunOne(task, L);
                    }
                    catch (Exception ex)
                    {
                        L("   FAILED " + ex.Message);
                        ok = false;
                    }
                    L(ok ? "   ok" : "   failed");
                    if (!ok) exit |= ResultFor(task.Name);
                }
            }
        }
        catch (Exception ex)
        {
            L("FATAL " + ex);
            exit |= AdminTaskResult.Other;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(cli.LogDir))
                {
                    Directory.CreateDirectory(cli.LogDir);
                    File.AppendAllText(Path.Combine(cli.LogDir, "admin-task.log"), sb.ToString());
                }
            }
            catch
            {
                // nothing sensible to do when even the log cannot be written
            }
        }
        return (int)exit;
    }

    private static AdminTaskResult ResultFor(string task) => task.ToLowerInvariant() switch
    {
        "acl-secure" => AdminTaskResult.Acl,
        "vcredist" => AdminTaskResult.VcRedist,
        "firewall-add" or "firewall-unblock" or "firewall-remove" => AdminTaskResult.Firewall,
        _ => AdminTaskResult.Other,
    };

    private static bool RunOne(AdminTaskRequest task, Action<string> log)
    {
        var args = task.Args;
        switch (task.Name.ToLowerInvariant())
        {
            case "firewall-add":
            {
                var port = int.Parse(args[0]);
                var scope = args.Count > 1 && Enum.TryParse<FirewallScope>(args[1], true, out var s) ? s : FirewallScope.LocalSubnet;
                FirewallManager.AddWebRule(port, scope);
                log($"   rule '{FirewallManager.RuleName(port)}' added (remote: {FirewallManager.RemoteFor(scope)})");
                return true;
            }
            case "firewall-unblock":
            {
                var removed = FirewallManager.RemoveBlockingRules(int.Parse(args[0]), args[1]);
                log($"   removed {removed} blocking rule(s)");
                return true;
            }
            case "firewall-remove":
            {
                var removed = FirewallManager.RemoveAllInkspireRules();
                log($"   removed {removed} rule(s)");
                return true;
            }
            case "vcredist":
            {
                var psi = new ProcessStartInfo(args[0]) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("/install");
                psi.ArgumentList.Add("/quiet");
                psi.ArgumentList.Add("/norestart");
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("The runtime installer did not start.");
                p.WaitForExit();
                // 3010 = success, reboot recommended; 1638 = a newer version is already installed
                log($"   installer exit code {p.ExitCode}");
                return p.ExitCode is 0 or 3010 or 1638;
            }
            case "acl-secure":
            {
                // SIDs, not names: works on every Windows language.
                var psi = new ProcessStartInfo("icacls.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add(args[0]);
                psi.ArgumentList.Add("/inheritance:r");
                psi.ArgumentList.Add("/grant:r");
                psi.ArgumentList.Add($"*{args[1]}:(OI)(CI)F");
                psi.ArgumentList.Add("*S-1-5-18:(OI)(CI)F");
                psi.ArgumentList.Add("*S-1-5-32-544:(OI)(CI)F");
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("icacls did not start.");
                var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                log("   " + output.Trim().ReplaceLineEndings(" | "));
                return p.ExitCode == 0;
            }
            default:
                log($"   unknown task {task.Name}");
                return false;
        }
    }
}
