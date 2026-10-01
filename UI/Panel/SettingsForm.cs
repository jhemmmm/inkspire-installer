using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Network;
using Inkspire.Panel.Runtime;
using Inkspire.Panel.UI.Wizard;

namespace Inkspire.Panel.UI.Panel;

/// <summary>Panel settings. Everything is applied together on OK; the firewall part needs one administrator prompt.</summary>
public sealed class SettingsForm : Form
{
    private sealed record AddressChoice(string Url, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppServices _ctx;
    private readonly TextBox _shop = FormRows.Text(300);
    private readonly ComboBox _address = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Width = 90 };
    private readonly NumericUpDown _pool = new() { Minimum = 1, Maximum = 32, Width = 90 };
    private readonly NumericUpDown _poolBase = new() { Minimum = 1024, Maximum = 65000, Width = 90 };
    private readonly NumericUpDown _upload = new() { Minimum = 8, Maximum = 2048, Width = 90 };
    private readonly NumericUpDown _recycle = new() { Minimum = 0, Maximum = 1_000_000, Width = 110 };
    private readonly CheckBox _firewall = new() { Text = "Let the other PCs connect (manage the Windows Firewall rule)", AutoSize = true };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly CheckBox _runAtLogin = new() { Text = "Start Inkspire when I sign in to Windows", AutoSize = true };
    private readonly CheckBox _tray = new() { Text = "Closing the window keeps Inkspire running in the notification area", AutoSize = true };
    private readonly CheckBox _confirmExit = new() { Text = "Ask before quitting the panel (it stops Inkspire for everyone)", AutoSize = true };
    private readonly CheckBox _awake = new() { Text = "Keep this PC awake while Inkspire runs", AutoSize = true };
    private readonly CheckBox _backup = new() { Text = "Back up the database automatically", AutoSize = true };
    private readonly NumericUpDown _backupHours = new() { Minimum = 1, Maximum = 720, Width = 70 };
    private readonly NumericUpDown _backupKeep = new() { Minimum = 1, Maximum = 365, Width = 70 };
    private readonly CheckBox _updates = new() { Text = "Check GitHub once a day for a new Inkspire version (never installs by itself)", AutoSize = true };

    public SettingsForm(AppServices ctx)
    {
        _ctx = ctx;
        Text = "Settings";
        Icon = AppIcon.Main;
        Width = 780;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };

        flow.Controls.Add(Section("Shop"));
        var shop = FormRows.Table(190);
        FormRows.Add(shop, "Shop name", _shop);
        FormRows.Add(shop, "Address other PCs use", _address, "Inkspire builds links in emails and file previews from this. Change it after the server's IP changes.");
        flow.Controls.Add(shop);

        flow.Controls.Add(Section("Network"));
        var net = FormRows.Table(190);
        FormRows.Add(net, "Web port", _port, "Changing it restarts Inkspire and updates the firewall rule.");
        FormRows.Add(net, "", _firewall);
        _scope.Items.AddRange(new object[]
        {
            "Only PCs on the same network (recommended)",
            "Any private address (10.x, 172.16-31.x, 192.168.x)",
            "Any address (not recommended)",
        });
        FormRows.Add(net, "Allowed from", _scope);
        flow.Controls.Add(net);

        flow.Controls.Add(Section("Performance and uploads"));
        var perf = FormRows.Table(190);
        FormRows.Add(perf, "PHP workers", _pool, "Each worker serves one request at a time. 6 is plenty for a few PCs; raise it if pages queue up.");
        FormRows.Add(perf, "First PHP worker port", _poolBase, "Workers use consecutive local ports from here (e.g. 9001-9006).");
        FormRows.Add(perf, "Largest upload (MB)", _upload, "The server-side ceiling. Inkspire's own file-size limit (Admin > Settings) must stay below it.");
        FormRows.Add(perf, "Recycle workers every", _recycle, "Requests per PHP worker before it restarts (0 = never). Each restart is a ~0.1 s gap for that worker.");
        flow.Controls.Add(perf);

        flow.Controls.Add(Section("Panel"));
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.AddRange(new Control[] { _runAtLogin, _tray, _confirmExit, _awake });
        flow.Controls.Add(panel);

        flow.Controls.Add(Section("Backups and updates"));
        var bk = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        bk.Controls.Add(_backup);
        bk.Controls.Add(new Label { Text = "every", AutoSize = true, Margin = new Padding(8, 4, 2, 0) });
        bk.Controls.Add(_backupHours);
        bk.Controls.Add(new Label { Text = "hours, keep", AutoSize = true, Margin = new Padding(2, 4, 2, 0) });
        bk.Controls.Add(_backupKeep);
        flow.Controls.Add(bk);
        flow.Controls.Add(_updates);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var ok = new Button { Text = "OK", Width = 90 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(flow);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = ok;
        ok.Click += (_, _) => Apply();
        _firewall.CheckedChanged += (_, _) => _scope.Enabled = _firewall.Checked;

        LoadFrom(ctx.Config);
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, 12, 0, 4),
    };

    private void LoadFrom(PanelConfig c)
    {
        _shop.Text = c.App.Name;
        _port.Value = c.Web.Port;
        _pool.Value = c.Web.PoolSize;
        _poolBase.Value = c.Web.PoolBasePort;
        _upload.Value = c.Web.MaxUploadMb;
        _recycle.Value = c.Web.MaxRequests;
        _firewall.Checked = c.Firewall.Enabled;
        _scope.SelectedIndex = (int)c.Firewall.Scope;
        _scope.Enabled = c.Firewall.Enabled;
        _runAtLogin.Checked = c.Ui.RunAtLogin;
        _tray.Checked = c.Ui.MinimizeToTray;
        _confirmExit.Checked = c.Ui.ConfirmStopOnExit;
        _awake.Checked = c.Ui.KeepAwake;
        _backup.Checked = c.Backup.Enabled;
        _backupHours.Value = c.Backup.IntervalHours;
        _backupKeep.Value = c.Backup.RetentionCount;
        _updates.Checked = c.Updates.AutoCheck;

        var port = (int)_port.Value;
        _address.Items.Clear();
        foreach (var a in LanAddresses.List())
            _address.Items.Add(new AddressChoice($"http://{a.Ip}:{port}", $"http://{a.Ip}:{port}   ({a.Adapter}{(a.IsDhcp ? ", DHCP" : "")})"));
        _address.Items.Add(new AddressChoice($"http://localhost:{port}", "This PC only (localhost)"));
        var current = c.App.AppUrl.TrimEnd('/');
        var index = _address.Items.Cast<AddressChoice>().ToList().FindIndex(a => string.Equals(a.Url.Split(':')[1], current.Split(':').ElementAtOrDefault(1), StringComparison.OrdinalIgnoreCase));
        _address.SelectedIndex = index >= 0 ? index : 0;
    }

    private void Apply()
    {
        var port = (int)_port.Value;
        var poolBase = (int)_poolBase.Value;
        var pool = (int)_pool.Value;
        if (port >= poolBase && port < poolBase + pool)
        {
            MessageBox.Show(this, $"Port {port} collides with the PHP worker ports {poolBase}-{poolBase + pool - 1}. Choose another.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (port != _ctx.Config.Web.Port && !Network.PortProbe.IsFree(port) && !_ctx.Services.Nginx.Status.IsActive)
        {
            var who = Network.PortProbe.WhoHolds(port);
            MessageBox.Show(this, $"Port {port} is already in use by {who?.ProcessName ?? "another program"}. {who?.Hint}", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_shop.Text))
        {
            MessageBox.Show(this, "Enter the shop name.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var after = ConfigStore.Clone(_ctx.Config);
        after.App.Name = _shop.Text.Trim();
        // The address list was built for the old port; rebuild the URL on the (possibly new) port.
        var chosen = (_address.SelectedItem as AddressChoice)?.Url ?? after.App.AppUrl;
        var host = new Uri(chosen).Host;
        after.App.AppUrl = $"http://{host}:{port}";
        after.Web.Port = port;
        after.Web.PoolSize = pool;
        after.Web.PoolBasePort = poolBase;
        after.Web.MaxUploadMb = (int)_upload.Value;
        after.Web.MaxRequests = (int)_recycle.Value;
        after.Firewall.Enabled = _firewall.Checked;
        after.Firewall.Scope = (FirewallScope)Math.Max(0, _scope.SelectedIndex);
        after.Ui.RunAtLogin = _runAtLogin.Checked;
        after.Ui.MinimizeToTray = _tray.Checked;
        after.Ui.ConfirmStopOnExit = _confirmExit.Checked;
        after.Ui.KeepAwake = _awake.Checked;
        after.Backup.Enabled = _backup.Checked;
        after.Backup.IntervalHours = (int)_backupHours.Value;
        after.Backup.RetentionCount = (int)_backupKeep.Value;
        after.Updates.AutoCheck = _updates.Checked;
        after.Normalize();

        SettingsApplier.Result? result = null;
        var ok = TaskDialog.Run(this, "Applying settings", async (log, ct) => result = await SettingsApplier.ApplyAsync(_ctx, after, log, ct), autoClose: true);
        if (!ok) return;

        PanelAppContext.Current?.RefreshAfterSettings();
        if (result is { FirewallDeclined: true })
            MessageBox.Show(this, "The other settings were saved, but the firewall was not changed because the administrator prompt was declined.\n\nOpen Network & clients and press Fix when you are ready.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        DialogResult = DialogResult.OK;
        Close();
    }
}
