using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Processes;
using Inkspire.Panel.Services;

namespace Inkspire.Panel.Setup;

/// <summary><c>InkspirePanel.exe --dev-smoke &lt;root&gt; [holdSeconds]</c>: starts every service of an installed tree, waits for the
/// web app, fetches /up and the login page, optionally holds the server up (for manual tests from another PC), stops
/// everything and checks that no nginx/php is left under the root. Exit code 0 = all good.</summary>
public static class DevSmoke
{
    public static int Run(string root, int holdSeconds)
    {
        ConsoleAttach.Attach();
        var paths = new AppPaths(root);
        Log.Configure(paths.LogsDir);
        var failures = new List<string>();

        void Say(string line)
        {
            Console.WriteLine(line);
            Log.Info(line);
        }

        try
        {
            var store = new ConfigStore(paths.PanelConfigFile);
            var ctx = AppServices.Initialize(paths, store, store.Load());
            var port = ctx.Config.Web.Port;

            var swept = ctx.Services.SweepOrphans();
            if (swept > 0) Say($"Killed {swept} leftover process(es)");

            Say("Starting services…");
            ctx.Services.StartAllAsync().GetAwaiter().GetResult();
            foreach (var s in ctx.Services.All) Say($"  {s.Title}: {s.Status.State} {s.Status.Detail}");

            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline && !ctx.Services.WebAppServing)
            {
                ctx.Services.PollOnceAsync().GetAwaiter().GetResult();
                Thread.Sleep(1000);
            }
            foreach (var s in ctx.Services.All) Say($"  {s.Title}: {s.Status.State} {s.Status.Detail}");
            if (!ctx.Services.WebAppServing) failures.Add("web app did not become healthy within 90 s");

            Check(ctx, $"http://127.0.0.1:{port}/up", 200, failures, Say);
            Check(ctx, $"http://127.0.0.1:{port}/login", 200, failures, Say);
            Check(ctx, $"http://127.0.0.1:{port}/", 200, failures, Say);
            // A static asset path that does not exist must be a clean 404, not a PHP error page.
            Check(ctx, $"http://127.0.0.1:{port}/build/does-not-exist.js", 404, failures, Say);
            // Only index.php may run PHP.
            Check(ctx, $"http://127.0.0.1:{port}/storage/x.php", 404, failures, Say);

            // Informational: what the Network & clients dialog would tell the user right now.
            var started = DateTime.UtcNow;
            var diagnosis = Network.NetworkDiagnostics.RunAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
            Say($"Network diagnosis ({(DateTime.UtcNow - started).TotalSeconds:0.0}s):");
            foreach (var r in diagnosis) Say($"  [{r.Level}] {r.Name}: {r.Detail}{(r.FixId is null ? "" : "  (fix: " + r.FixId + ")")}");

            if (holdSeconds > 0)
            {
                Say($"Holding for {holdSeconds} s (try it from another PC now)…");
                Thread.Sleep(TimeSpan.FromSeconds(holdSeconds));
            }

            Say("Stopping services…");
            ctx.Services.StopAllAsync().GetAwaiter().GetResult();
            Thread.Sleep(1500);
            var left = ProcessSweep.Sweep(paths.InstallRoot);
            if (left > 0) failures.Add($"{left} process(es) were still running after Stop all");
            ctx.Dispose();
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
            Log.Error("dev-smoke failed", ex);
        }

        Say(failures.Count == 0 ? "SMOKE OK" : "SMOKE FAILED:\n  " + string.Join("\n  ", failures));
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(AppServices ctx, string url, int expected, List<string> failures, Action<string> say)
    {
        try
        {
            using var response = ctx.Http.GetAsync(url).GetAwaiter().GetResult();
            var code = (int)response.StatusCode;
            say($"  GET {url} -> {code}");
            if (code != expected) failures.Add($"{url} answered {code}, expected {expected}");
        }
        catch (Exception ex)
        {
            failures.Add($"{url}: {ex.Message}");
        }
    }
}
