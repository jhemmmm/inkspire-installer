using System.Diagnostics;
using System.Text;
using Inkspire.Panel.App;

namespace Inkspire.Panel.Runtime;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
    public string Combined => (StdOut + (StdErr.Length > 0 ? Environment.NewLine + StdErr : "")).Trim();
}

/// <summary>Runs php.exe (CLI) with the same environment the pool uses. Never opens a window, never prompts.</summary>
public sealed class PhpRunner
{
    private readonly AppPaths _paths;
    private readonly int _maxRequests;

    public PhpRunner(AppPaths paths, int maxRequests = 500)
    {
        _paths = paths;
        _maxRequests = maxRequests;
    }

    /// <param name="extraEnv">Per-call environment (e.g. secrets that must not appear on the command line).</param>
    public async Task<CommandResult> RunAsync(
        IEnumerable<string> args,
        CancellationToken ct,
        TimeSpan? timeout = null,
        Action<string>? onLine = null,
        string? workingDir = null,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo(_paths.PhpExe)
        {
            WorkingDirectory = workingDir ?? _paths.AppDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var argList = args.ToList();
        foreach (var a in argList) psi.ArgumentList.Add(a);

        foreach (var (k, v) in PhpEnvironment.Build(_paths, _maxRequests))
        {
            if (k == "PATH") psi.Environment["PATH"] = v + ";" + (psi.Environment.TryGetValue("PATH", out var cur) ? cur : "");
            else psi.Environment[k] = v;
        }
        if (extraEnv is not null)
            foreach (var (k, v) in extraEnv) psi.Environment[k] = v;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is null) return; stdout.AppendLine(e.Data); onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is null) return; stderr.AppendLine(e.Data); onLine?.Invoke(e.Data); };

        Log.Info($"run: php {string.Join(' ', argList.Select(a => a.Length > 120 ? a[..120] + "..." : a).Select(a => a.Contains(' ') ? "\"" + a + "\"" : a))}");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"php {string.Join(' ', argList.Take(3))} did not finish in time.");
        }
        p.WaitForExit(); // flush the redirected streams
        return new CommandResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }
}

/// <summary>Typed artisan commands. Always non-interactive and ANSI-free.</summary>
public sealed class ArtisanRunner
{
    private readonly PhpRunner _php;
    private readonly AppPaths _paths;

    public ArtisanRunner(AppPaths paths, PhpRunner php)
    {
        _paths = paths;
        _php = php;
    }

    public Task<CommandResult> RunAsync(CancellationToken ct, Action<string>? onLine, params string[] artisanArgs)
    {
        var args = new List<string> { _paths.AppArtisan };
        args.AddRange(artisanArgs);
        args.Add("-n");
        args.Add("--no-ansi");
        return _php.RunAsync(args, ct, TimeSpan.FromMinutes(15), onLine);
    }

    public Task<CommandResult> KeyGenerate(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "key:generate", "--force");
    public Task<CommandResult> Migrate(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "migrate", "--force");
    public Task<CommandResult> Optimize(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "optimize");
    public Task<CommandResult> OptimizeClear(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "optimize:clear");
    public Task<CommandResult> ConfigCache(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "config:cache");
    public Task<CommandResult> ViewCache(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "view:cache");
    public Task<CommandResult> ScheduleList(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "schedule:list");
    public Task<CommandResult> About(CancellationToken ct) => RunAsync(ct, null, "about");

    /// <summary>Throws with the command output when the exit code is non-zero.</summary>
    public static CommandResult Require(CommandResult r, string what)
    {
        if (!r.Ok) throw new InvalidOperationException($"{what} failed (exit {r.ExitCode}):\n{r.Combined}");
        return r;
    }
}
