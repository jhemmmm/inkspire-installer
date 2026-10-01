using Inkspire.Panel.App;
using Inkspire.Panel.Network;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.UI.Panel;

/// <summary>"Why can't the other PCs connect?": the addresses to hand out, a live diagnosis (nginx, firewall rule, blocking
/// rules, network type, DHCP, APP_URL, answers on the LAN address) and one button that fixes what it can.</summary>
public sealed class NetworkClientsForm : Form
{
    private readonly AppServices _ctx;
    private readonly ListView _urls = new() { View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, Height = 110, Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle };
    private readonly ListView _checks = new() { View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, ShowItemToolTips = true };
    private readonly Button _again = new() { Text = "Check again", AutoSize = true };
    private readonly Button _fix = new() { Text = "Fix problems (administrator)", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 8, 0, 0) };
    private IReadOnlyList<CheckResult> _results = Array.Empty<CheckResult>();
    private int _generation;

    public NetworkClientsForm(AppServices ctx)
    {
        _ctx = ctx;
        Text = "Network & clients";
        Icon = AppIcon.Main;
        Width = 860;
        Height = 660;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;

        _urls.Columns.Add("", 640);
        _urls.Columns.Add("", 160);
        _checks.Columns.Add("", 30);
        _checks.Columns.Add("", 170);
        _checks.Columns.Add("", 600);

        var top = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 44 };
        top.Controls.Add(new Label
        {
            Text = "Type one of these addresses in the browser of each other PC:",
            AutoSize = true, Left = 12, Top = 14, Font = new Font(Font, FontStyle.Bold),
        });

        var copyRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4) };
        var copyUrl = new Button { Text = "Copy selected address", AutoSize = true };
        var copyText = new Button { Text = "Copy instructions for the other PCs", AutoSize = true };
        var saveUrl = new Button { Text = "Save Inkspire.url shortcut…", AutoSize = true };
        copyRow.Controls.AddRange(new Control[] { copyUrl, copyText, saveUrl });

        var checksLabel = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 34 };
        checksLabel.Controls.Add(new Label { Text = "Checks", AutoSize = true, Left = 12, Top = 10, Font = new Font(Font, FontStyle.Bold) });

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        var close = new Button { Text = "Close", Width = 90, DialogResult = DialogResult.Cancel };
        bottom.Controls.AddRange(new Control[] { _again, _fix, _status });

        Controls.Add(_checks);
        Controls.Add(checksLabel);
        Controls.Add(copyRow);
        Controls.Add(_urls);
        Controls.Add(top);
        Controls.Add(bottom);
        CancelButton = close;

        copyUrl.Click += (_, _) =>
        {
            if (_urls.SelectedItems.Count > 0 && Ui.TryCopy(_urls.SelectedItems[0].Text)) _status.Text = "Address copied.";
        };
        copyText.Click += (_, _) =>
        {
            if (Ui.TryCopy(NetworkDiagnostics.ClientInstructions(_ctx.Config, LanAddresses.List()))) _status.Text = "Instructions copied: paste them into a message.";
        };
        saveUrl.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { FileName = "Inkspire.url", Filter = "Internet shortcut (*.url)|*.url", Title = "Save the shortcut for the other PCs" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dlg.FileName, NetworkDiagnostics.UrlShortcut(PrimaryUrl()));
            _status.Text = "Saved. Copy it to the other PCs (USB stick or shared folder).";
        };
        _again.Click += (_, _) => RunChecks();
        _fix.Click += async (_, _) => await FixAsync();

        FillUrls();
        Shown += (_, _) => RunChecks();
    }

    private string PrimaryUrl()
    {
        var ip = LanAddresses.Primary()?.Ip;
        return ip is null ? $"http://localhost:{_ctx.Config.Web.Port}/" : $"http://{ip}:{_ctx.Config.Web.Port}/";
    }

    private void FillUrls()
    {
        _urls.Items.Clear();
        var port = _ctx.Config.Web.Port;
        foreach (var a in LanAddresses.List())
        {
            var item = new ListViewItem($"http://{a.Ip}:{port}/");
            item.SubItems.Add(a.IsDhcp ? $"{a.Adapter}, DHCP (may change)" : $"{a.Adapter}, fixed");
            _urls.Items.Add(item);
        }
        var host = new ListViewItem($"http://{Environment.MachineName}:{port}/");
        host.SubItems.Add("computer name");
        _urls.Items.Add(host);
        if (_urls.Items.Count > 0) _urls.Items[0].Selected = true;
    }

    private void RunChecks()
    {
        var generation = ++_generation;
        _again.Enabled = false;
        _fix.Enabled = false;
        _status.Text = "Checking…";
        _ = Task.Run(async () =>
        {
            IReadOnlyList<CheckResult> results;
            try
            {
                results = await NetworkDiagnostics.RunAsync(_ctx, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                results = new[] { new CheckResult("Diagnosis", CheckLevel.Warning, "Could not run the checks: " + ex.Message) };
            }
            UiThread.Post(() =>
            {
                if (IsDisposed || generation != _generation) return;
                Render(results);
            });
        });
    }

    private void Render(IReadOnlyList<CheckResult> results)
    {
        _results = results;
        _checks.BeginUpdate();
        _checks.Items.Clear();
        foreach (var r in results)
        {
            var (glyph, color) = r.Level switch
            {
                CheckLevel.Ok => ("✔", Ui.Green),
                CheckLevel.Info => ("ℹ", Color.SteelBlue),
                CheckLevel.Warning => ("⚠", Ui.Amber),
                _ => ("✖", Ui.Red),
            };
            var item = new ListViewItem(glyph) { ForeColor = color, ToolTipText = r.Detail };
            item.SubItems.Add(r.Name);
            item.SubItems.Add(r.Detail);
            _checks.Items.Add(item);
        }
        _checks.EndUpdate();

        var fixable = results.Where(r => r.FixId is not null && r.Level >= CheckLevel.Warning).ToList();
        var problems = results.Count(r => r.Level == CheckLevel.Blocker);
        _again.Enabled = true;
        _fix.Enabled = fixable.Count > 0;
        _status.ForeColor = problems > 0 ? Ui.Red : Color.DimGray;
        _status.Text = problems > 0 ? $"{problems} problem(s) would stop the other PCs." : "No problems found.";
    }

    private async Task FixAsync()
    {
        var ids = _results.Where(r => r.FixId is not null && r.Level >= CheckLevel.Warning).Select(r => r.FixId!).Distinct().ToList();
        if (ids.Count == 0) return;
        _fix.Enabled = false;
        var port = _ctx.Config.Web.Port;
        var tasks = new List<AdminTaskRequest>();
        if (ids.Contains("unblock")) tasks.Add(new("firewall-unblock", new[] { port.ToString(), _ctx.Paths.NginxExe }));
        if (ids.Contains("firewall")) tasks.Add(new("firewall-add", new[] { port.ToString(), _ctx.Config.Firewall.Scope.ToString() }));

        if (tasks.Count > 0)
        {
            _status.Text = "Waiting for the Windows administrator prompt…";
            var code = await Elevation.RunAdminTasksAsync(tasks, _ctx.Paths.LogsDir, CancellationToken.None);
            if (code == Elevation.ExitCancelled)
            {
                _status.Text = "Cancelled: the firewall was not changed.";
                RunChecks();
                return;
            }
            if (code != 0) MessageBox.Show(this, $"Windows refused part of the change. Details: {_ctx.Paths.AdminTaskLog}", "Network & clients", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (ids.Contains("appurl") && LanAddresses.Primary() is { } primary)
        {
            TaskDialog.Run(this, "Updating Inkspire's address", (log, ct) =>
                AppRuntime.ChangeAppUrlAsync(_ctx, $"http://{primary.Ip}:{port}", log, ct));
        }
        RunChecks();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _generation++;
        base.OnFormClosed(e);
    }
}
