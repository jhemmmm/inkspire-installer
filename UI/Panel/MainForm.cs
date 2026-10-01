using Inkspire.Panel.App;
using Inkspire.Panel.Network;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.UI.Panel;

/// <summary>The control panel window: toolbar, one row per service, log viewer, address bar.</summary>
public sealed class MainForm : Form
{
    private readonly PanelAppContext _app;
    private readonly AppServices _ctx;
    private readonly List<ServiceRowControl> _rows = new();
    private readonly LogViewerControl _logs;
    private readonly ToolStripStatusLabel _addressLabel;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly ToolStrip _tools;
    private readonly ToolStripButton _updates;

    public MainForm(PanelAppContext app)
    {
        _app = app;
        _ctx = app.Ctx;

        Width = 1120;
        Height = 720;
        MinimumSize = new Size(960, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Main;

        _tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        AddButton("Start all", () => _ = _app.StartAllAsync());
        AddButton("Stop all", () => _ = _app.StopAllAsync());
        _tools.Items.Add(new ToolStripSeparator());
        AddButton("Open Inkspire", _app.OpenApp, "Open Inkspire in your browser on this PC");
        AddButton("Network && clients", ShowNetwork, "Addresses for the other PCs, firewall check and fixes");
        _tools.Items.Add(new ToolStripSeparator());
        AddButton("Backup now", BackupNow, "Copy the database and settings to the backups folder");
        _updates = AddButton("Updates", ShowUpdates, "Check for and install a newer Inkspire version");
        _tools.Items.Add(new ToolStripSeparator());
        AddButton("Edit .env", () => { using var f = new EnvEditorForm(_ctx); f.ShowDialog(this); }, "Edit Inkspire's environment settings (email, payments)");
        AddButton("Settings", ShowSettings);
        AddButton("Re-run setup", () => _ = _app.RerunSetupAsync(this), "Open the setup wizard again with the current settings; the database is kept");
        _tools.Items.Add(new ToolStripSeparator());
        var folders = new ToolStripDropDownButton("Folders") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        folders.DropDownItems.Add("Install folder", null, (_, _) => Ui.OpenExternal(_ctx.Paths.InstallRoot));
        folders.DropDownItems.Add("Uploaded files", null, (_, _) => Ui.OpenExternal(Path.Combine(_ctx.Paths.DataStorageDir, "app")));
        folders.DropDownItems.Add("Backups", null, (_, _) => { Directory.CreateDirectory(_ctx.Paths.BackupsDir); Ui.OpenExternal(_ctx.Paths.BackupsDir); });
        folders.DropDownItems.Add("Logs", null, (_, _) => Ui.OpenExternal(_ctx.Paths.LogsDir));
        _tools.Items.Add(folders);

        var rowsPanel = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Gainsboro,
            Padding = new Padding(0, 0, 0, 1),
        };
        // Docked controls stack bottom-up, so add in reverse to read PHP → nginx → Scheduler top to bottom.
        foreach (var service in _ctx.Services.All.Reverse())
        {
            if (!service.Enabled) continue;
            var row = new ServiceRowControl(service);
            row.LogsRequested += s => _logs!.ShowService(s);
            rowsPanel.Controls.Add(row);
            rowsPanel.Controls.Add(new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.Gainsboro });
            _rows.Add(row);
        }

        _logs = new LogViewerControl();
        _logs.SetServices(_ctx.Services.Enabled);

        var status = new StatusStrip();
        _addressLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        status.Items.Add(_addressLabel);
        status.Items.Add(new ToolStripStatusLabel(
            $"Panel {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}  ·  Inkspire {(string.IsNullOrEmpty(_ctx.Config.Versions.App) ? "?" : _ctx.Config.Versions.App)}  ·  {_ctx.Paths.InstallRoot}")
        { ForeColor = Color.DimGray });

        Controls.Add(_logs);
        Controls.Add(rowsPanel);
        Controls.Add(_tools);
        Controls.Add(status);

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) =>
        {
            foreach (var r in _rows) r.Tick();
        };
        _tick.Start();

        RefreshTexts();
        RefreshUpdatesBadge(_app.LastUpdate);
        _app.UpdateInfoChanged += info => UiThread.Post(() => { if (!IsDisposed) RefreshUpdatesBadge(info); });
    }

    private ToolStripButton AddButton(string text, Action onClick, string? tooltip = null)
    {
        var b = new ToolStripButton(text, null, (_, _) => onClick()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        if (tooltip is not null) b.ToolTipText = tooltip;
        _tools.Items.Add(b);
        return b;
    }

    private void ShowNetwork()
    {
        using var f = new NetworkClientsForm(_ctx);
        f.ShowDialog(this);
        RefreshTexts();
    }

    private void ShowSettings()
    {
        using var f = new SettingsForm(_ctx);
        f.ShowDialog(this);
        RefreshTexts();
    }

    private void ShowUpdates()
    {
        using var f = new UpdatesForm(_ctx, _app.LastUpdate);
        f.ShowDialog(this);
    }

    private void BackupNow()
    {
        var ok = TaskDialog.Run(this, "Database backup", async (log, ct) =>
        {
            var backup = new SqliteBackup(_ctx.Paths, _ctx.Config);
            await backup.RunAsync(ct, log);
            _ctx.SaveConfig();
        });
        if (ok) Ui.OpenExternal(_ctx.Paths.BackupsDir);
    }

    private void RefreshUpdatesBadge(UpdateInfo? info)
    {
        var available = info?.Available == true;
        _updates.Text = available ? "Updates (1)" : "Updates";
        _updates.ForeColor = available ? Ui.Red : SystemColors.ControlText;
        _updates.Font = new Font(Font, available ? FontStyle.Bold : FontStyle.Regular);
    }

    /// <summary>Title and address bar follow the shop name / port / addresses (after Settings or a network change).</summary>
    public void RefreshTexts()
    {
        Text = $"Inkspire Control Panel - {_ctx.Config.App.Name}";
        var port = _ctx.Config.Web.Port;
        var addrs = LanAddresses.List();
        _addressLabel.Text = addrs.Count == 0
            ? $"This PC: http://localhost:{port}   (no network address detected)"
            : $"This PC: http://localhost:{port}   ·   Other PCs: " + string.Join("  |  ", addrs.Select(a => $"http://{a.Ip}:{port}"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }
}
