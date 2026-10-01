using System.Security.Principal;
using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.Setup;

public sealed record StepProgress(string Step, string Message, double? Fraction);

/// <summary>The ordered install steps. Every step can be run again (Repair, a cancelled install), none touches existing
/// data: the database, uploads and .env survive, and the first-run data is only created while <c>Seeded</c> is false.</summary>
public sealed class InstallEngine
{
    private readonly WizardState _s;
    private readonly IProgress<StepProgress> _progress;
    private readonly Action<string> _log;

    public InstallEngine(WizardState state, IProgress<StepProgress> progress, Action<string> log)
    {
        _s = state;
        _progress = progress;
        _log = log;
    }

    /// <summary>Developer harness: no pointer file, Run key or shortcut, so a scratch install never hijacks the real one.</summary>
    public bool SkipRegistration { get; init; }

    /// <summary>Developer harness: do not raise a UAC prompt (the firewall rule and the VC++ runtime are skipped).</summary>
    public bool SkipElevation { get; init; }

    private AppPaths Paths => new(_s.InstallRoot);

    public async Task RunAsync(CancellationToken ct)
    {
        var steps = new List<(string Name, Func<CancellationToken, Task> Run)>
        {
            ("Preparing folders", PrepareFoldersAsync),
            ("Windows permissions (one administrator prompt)", AdminTasksAsync),
            ("Copying the control panel", CopySelfAsync),
            ("Installing PHP", ct2 => ExtractAsync("php", Paths.PhpDir, ct2)),
            ("Installing nginx", ct2 => ExtractAsync("nginx", Paths.NginxDir, ct2)),
            ("Installing Inkspire", ct2 => ExtractAsync("app", Paths.AppDir, ct2)),
            ("Installing certificates", InstallCaBundleAsync),
            ("Writing configuration", WriteConfigsAsync),
            ("Setting up the database", DatabaseAsync),
        };
        if (!SkipRegistration) steps.Add(("Registering the panel", RegisterAsync));

        for (var i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (name, run) = steps[i];
            _progress.Report(new StepProgress(name, "", (double)i / steps.Count));
            _log($"== {name}");
            await run(ct).ConfigureAwait(false);
        }
        _progress.Report(new StepProgress("Finished", "", 1));
    }

    private Task PrepareFoldersAsync(CancellationToken ct)
    {
        var paths = Paths;
        Directory.CreateDirectory(paths.InstallRoot);
        Directory.CreateDirectory(paths.LogsDir);
        Log.Configure(paths.LogsDir);
        return Task.CompletedTask;
    }

    private async Task AdminTasksAsync(CancellationToken ct)
    {
        var paths = Paths;
        var tasks = new List<AdminTaskRequest>();
        if (!_s.IsRepair)
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (sid is not null) tasks.Add(new("acl-secure", new[] { paths.InstallRoot, sid }));
        }
        if (_s.NeedVcRedist && _s.Downloaded.TryGetValue("vcredist", out var vc)) tasks.Add(new("vcredist", new[] { vc }));
        if (_s.FirewallEnabled) tasks.Add(new("firewall-add", new[] { _s.WebPort.ToString(), _s.FirewallScope.ToString() }));

        if (SkipElevation || tasks.Count == 0)
        {
            _log(SkipElevation ? "Skipped (developer run)" : "Nothing to do");
            return;
        }

        _log("Asking Windows for administrator approval: " + string.Join(", ", tasks.Select(t => t.Name)));
        var code = await Elevation.RunAdminTasksAsync(tasks, paths.LogsDir, ct).ConfigureAwait(false);
        if (code == Elevation.ExitCancelled)
        {
            throw new OperationCanceledException(
                "Administrator approval was declined. The firewall rule and runtime components were not installed. Run setup again and choose Yes on the Windows prompt.");
        }

        var result = (AdminTaskResult)code;
        if (result.HasFlag(AdminTaskResult.NotElevated))
            throw new InvalidOperationException("Windows did not grant administrator rights. Run setup again and approve the prompt.");
        if (result.HasFlag(AdminTaskResult.VcRedist))
            throw new InvalidOperationException("The Visual C++ runtime could not be installed, and PHP cannot run without it. See logs\\admin-task.log.");
        if (result.HasFlag(AdminTaskResult.Firewall))
        {
            _s.FirewallFailed = true;
            _log("The firewall rule could not be added. The other PCs will not be able to connect until it is: use Network & Clients > Fix in the panel.");
        }
        if (result.HasFlag(AdminTaskResult.Acl)) _log("The install folder permissions could not be tightened (see logs\\admin-task.log). Continuing.");
        if (result == AdminTaskResult.None) _log("Administrator tasks completed");
    }

    private Task CopySelfAsync(CancellationToken ct)
    {
        var paths = Paths;
        Directory.CreateDirectory(paths.PanelDir);
        var src = Environment.ProcessPath!;
        var dst = paths.PanelExe;
        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;

        // A running exe cannot be overwritten but it can be renamed.
        if (File.Exists(dst))
        {
            var old = dst + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { /* still running from a previous run */ }
            try { File.Move(dst, old); } catch { File.Delete(dst); }
        }
        File.Copy(src, dst, overwrite: true);
        _log($"Copied the panel to {dst}");
        return Task.CompletedTask;
    }

    private async Task ExtractAsync(string id, string dest, CancellationToken ct)
    {
        var paths = Paths;

        // Repair keeps working code: the bundle is only unpacked again when there is none (and was not even downloaded).
        if (id == "app" && _s.IsRepair && File.Exists(paths.AppArtisan))
        {
            _log("Inkspire is already installed here; keeping it (use Updates to change versions)");
            return;
        }

        var payload = _s.Payloads[id];
        var zip = _s.Downloaded[id];
        var marker = Path.Combine(dest, ".installed-" + payload.Version);

        if (id == "app")
        {
            string? envBackup = null;
            if (File.Exists(paths.AppEnvFile))
            {
                envBackup = Path.Combine(paths.ConfigDir, ".env.repair-backup");
                Directory.CreateDirectory(paths.ConfigDir);
                File.Copy(paths.AppEnvFile, envBackup, overwrite: true);
            }
            await Task.Run(() => FolderOps.DeleteTree(dest), ct).ConfigureAwait(false);
            await ExtractZipAsync(zip, dest, payload, ct).ConfigureAwait(false);
            if (envBackup is not null) File.Copy(envBackup, paths.AppEnvFile, overwrite: true);
            if (!File.Exists(paths.AppArtisan))
                throw new InvalidDataException("That zip is not an Inkspire app bundle (artisan is missing).");
        }
        else
        {
            if (File.Exists(marker))
            {
                _log($"{payload.Title} is already installed");
                return;
            }
            await ExtractZipAsync(zip, dest, payload, ct).ConfigureAwait(false);
        }

        foreach (var old in Directory.GetFiles(dest, ".installed-*")) File.Delete(old);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
        _log($"{payload.Title} installed");
    }

    private Task ExtractZipAsync(string zip, string dest, Payload payload, CancellationToken ct) =>
        Task.Run(() => ZipExtractor.Extract(zip, dest, payload.StripTopLevel,
            new Progress<(int Done, int Total)>(p => _progress.Report(new StepProgress(payload.Title, $"{p.Done}/{p.Total} files", null))), ct), ct);

    private Task InstallCaBundleAsync(CancellationToken ct)
    {
        File.Copy(_s.Downloaded["cacert"], Paths.CaBundle, overwrite: true);
        return Task.CompletedTask;
    }

    private Task WriteConfigsAsync(CancellationToken ct)
    {
        var paths = Paths;
        var store = new ConfigStore(paths.PanelConfigFile);
        var config = _s.ToPanelConfig(store.Exists ? TryLoad(store) : null);
        store.Save(config);

        var provisioner = new Provisioner(paths, config, _log);
        provisioner.EnsureDirectories();
        provisioner.EnsureJunctions();
        provisioner.WriteRuntimeConfigs();
        provisioner.WriteEnv(_s.IntegrationEnv());
        return Task.CompletedTask;
    }

    private static PanelConfig? TryLoad(ConfigStore store)
    {
        try { return store.Load(); }
        catch { return null; }
    }

    private async Task DatabaseAsync(CancellationToken ct)
    {
        var paths = Paths;
        var store = new ConfigStore(paths.PanelConfigFile);
        var config = store.Load();
        var provisioner = new Provisioner(paths, config, _log);

        await provisioner.EnsureSqliteAsync(ct).ConfigureAwait(false);
        await provisioner.BootstrapLaravelAsync(_s.Admin, ct).ConfigureAwait(false);
        store.Save(config); // keeps Seeded = true

        // The password was needed only for the account; do not keep it around.
        _s.AdminPassword = "";
    }

    private Task RegisterAsync(CancellationToken ct)
    {
        var paths = Paths;
        InstallLocator.WritePointer(paths.InstallRoot);
        StartupRegistration.Apply(true, paths);
        try
        {
            Shortcuts.Create(Shortcuts.StartMenuLink, paths);
            _log("Start-menu shortcut created");
        }
        catch (Exception ex)
        {
            _log("Shortcut not created: " + ex.Message);
        }
        return Task.CompletedTask;
    }
}
