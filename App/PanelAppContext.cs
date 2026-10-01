using System.Diagnostics;
using Inkspire.Panel.Network;
using Inkspire.Panel.Runtime;
using Inkspire.Panel.UI;
using Inkspire.Panel.UI.Panel;
using Microsoft.Win32;

namespace Inkspire.Panel.App;

/// <summary>Owns the tray icon, the main window, the maintenance timer and the service manager for the lifetime of panel mode.</summary>
public sealed class PanelAppContext : ApplicationContext
{
    private readonly CommandLine _cli;
    private readonly SingleInstance _instance;
    private readonly AppServices _ctx;
    private readonly TrayIcon _tray;
    private readonly System.Threading.Timer _maintenance;
    private MainForm? _main;
    private bool _exiting;
    private int _maintenanceRunning;
    private string _notifiedAddressDrift = "";

    /// <summary>The running panel, for code that needs the tray/main window (updater, settings).</summary>
    public static PanelAppContext? Current { get; private set; }

    /// <summary>Result of the latest "is there a newer Inkspire" lookup (null = unknown or already up to date).</summary>
    public UpdateInfo? LastUpdate { get; private set; }

    public event Action<UpdateInfo?>? UpdateInfoChanged;

    public PanelAppContext(CommandLine cli, SingleInstance instance)
    {
        _cli = cli;
        _instance = instance;
        _ctx = AppServices.Current;
        Current = this;
        UiThread.Capture();

        _tray = new TrayIcon(this);
        _instance.ListenForShow(() => UiThread.Post(ShowMain));
        SystemEvents.SessionEnding += OnSessionEnding;

        var swept = _ctx.Services.SweepOrphans();
        if (swept > 0) Log.Warn($"Killed {swept} orphaned nginx/php process(es) left over from a previous session.");

        _ctx.Services.AnyStatusChanged += _ => UiThread.Post(OnServiceChanged);
        _ctx.Services.StartPolling();
        _maintenance = new System.Threading.Timer(_ => MaintenanceTick(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

        if (!cli.Autostart) ShowMain();

        if (cli.Autostart || cli.Show)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ctx.Services.StartAllAsync(onlyAutostart: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error("Autostart failed", ex);
                }
            });
        }
        OnServiceChanged();
    }

    public AppServices Ctx => _ctx;

    private void OnServiceChanged()
    {
        if (_exiting) return;
        _tray.RefreshState(_ctx.Services);
        KeepAwake.Set(_ctx.Config.Ui.KeepAwake && _ctx.Services.AnyRunning);
    }

    /// <summary>Settings changed: tray text, keep-awake and the window's texts follow.</summary>
    public void RefreshAfterSettings()
    {
        OnServiceChanged();
        _main?.RefreshTexts();
    }

    public void SetUpdateInfo(UpdateInfo? info)
    {
        LastUpdate = info is { Available: true } ? info : null;
        UpdateInfoChanged?.Invoke(LastUpdate);
    }

    // ------------------------------------------------------------------ maintenance (every 10 minutes)

    private void MaintenanceTick()
    {
        if (_exiting || Interlocked.Exchange(ref _maintenanceRunning, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await LogRotation.RotateNginxAsync(_ctx).ConfigureAwait(false);
                await BackupIfDueAsync().ConfigureAwait(false);
                await CheckForUpdateIfDueAsync().ConfigureAwait(false);
                CheckAddressDrift();
            }
            catch (Exception ex)
            {
                Log.Error("Maintenance failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _maintenanceRunning, 0);
            }
        });
    }

    private async Task BackupIfDueAsync()
    {
        var backup = new SqliteBackup(_ctx.Paths, _ctx.Config);
        if (!backup.IsDue() || !File.Exists(_ctx.Paths.SqliteDb)) return;
        Log.Info("Scheduled backup starting");
        await backup.RunAsync(CancellationToken.None, Log.Info).ConfigureAwait(false);
        _ctx.SaveConfig();
    }

    private async Task CheckForUpdateIfDueAsync()
    {
        if (!UpdateChecker.IsDue(_ctx)) return;
        var info = await UpdateChecker.CheckAsync(_ctx, CancellationToken.None).ConfigureAwait(false);
        UiThread.Post(() => SetUpdateInfo(info));
        if (info.Available && info.LatestTag != _ctx.Config.Updates.NotifiedTag)
        {
            _ctx.Config.Updates.NotifiedTag = info.LatestTag;
            _ctx.SaveConfig();
            UiThread.Post(() => _tray.ShowBalloon($"Inkspire {info.LatestTag} is available. Open the panel and press Updates."));
        }
    }

    /// <summary>The server's address changed (DHCP): bookmarks and Inkspire's own links are stale. Tell the user once per new address.</summary>
    private void CheckAddressDrift()
    {
        if (!_ctx.Config.Firewall.Enabled) return;
        var addresses = LanAddresses.List();
        if (addresses.Count == 0) return;
        if (!Uri.TryCreate(_ctx.Config.App.AppUrl, UriKind.Absolute, out var uri) || !System.Net.IPAddress.TryParse(uri.Host, out _)) return;
        if (addresses.Any(a => a.Ip == uri.Host)) return;

        var now = addresses[0].Ip;
        if (_notifiedAddressDrift == now) return;
        _notifiedAddressDrift = now;
        UiThread.Post(() => _tray.ShowBalloon($"This PC's address changed to {now}. Open Network & clients to update Inkspire and tell the other PCs."));
    }

    // ------------------------------------------------------------------ window and lifecycle

    public void ShowMain()
    {
        if (_exiting) return;
        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(this);
            _main.FormClosing += OnMainClosing;
        }
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.BringToFront();
    }

    private void OnMainClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting) return;
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _ctx.Services.KillAllNow();
            return;
        }
        if (e.CloseReason == CloseReason.UserClosing && _ctx.Config.Ui.MinimizeToTray)
        {
            e.Cancel = true;
            _main!.Hide();
            _tray.ShowBalloonOnce("Inkspire keeps running here. Right-click the icon for options, or Exit to stop the server.");
            return;
        }
        e.Cancel = true;
        _ = ExitAsync();
    }

    public async Task StartAllAsync()
    {
        try { await _ctx.Services.StartAllAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Error("Start all failed", ex); }
    }

    public async Task StopAllAsync()
    {
        try { await _ctx.Services.StopAllAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Error("Stop all failed", ex); }
    }

    public void OpenApp() => Ui.OpenExternal(Ui.BrowserUrl(_ctx));

    /// <summary>Stops everything and reopens this exe as the setup wizard, pre-filled from the current settings.</summary>
    public async Task RerunSetupAsync(IWin32Window owner)
    {
        var answer = MessageBox.Show(owner,
            "This stops Inkspire for everyone and opens the setup wizard again with your current settings filled in.\nYour database, uploads and settings are kept.\n\nContinue?",
            "Re-run setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        _exiting = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _ctx.Services.StopAllAsync(cts.Token).ConfigureAwait(true);
            _instance.Release();
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "--wizard", "--install-root", _ctx.Paths.InstallRoot },
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the wizard", ex);
            MessageBox.Show(owner, "The wizard could not be started: " + ex.Message, "Re-run setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Shutdown();
        }
    }

    /// <summary>Stops everything (after confirmation when services run) and exits the panel.</summary>
    public async Task ExitAsync()
    {
        if (_exiting) return;
        if (_ctx.Services.AnyRunning && _ctx.Config.Ui.ConfirmStopOnExit)
        {
            var answer = MessageBox.Show(
                "Quitting the panel stops Inkspire for every PC on the network.\n\nStop all services and quit?",
                "Inkspire Control Panel", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }
        _exiting = true;
        try
        {
            _tray.SetText("Stopping services...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _ctx.Services.StopAllAsync(cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Stop-all during exit: {ex.Message}");
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        // About 5 s budget: no waiting. The job object finishes whatever is left.
        _exiting = true;
        try { _ctx.Services.KillAllNow(); }
        catch { /* shutting down */ }
    }

    private void Shutdown()
    {
        SystemEvents.SessionEnding -= OnSessionEnding;
        _maintenance.Dispose();
        KeepAwake.Set(false);
        try { _ctx.Dispose(); } catch { /* exiting */ }
        _tray.Dispose();
        _main?.Dispose();
        _instance.Release();
        ExitThread();
    }
}
