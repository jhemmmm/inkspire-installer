using Inkspire.Panel.App;
using Inkspire.Panel.Runtime;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Panel;

/// <summary>Installed vs latest Inkspire version, and the buttons to move to the newer one (or to a file you have).</summary>
public sealed class UpdatesForm : Form
{
    private readonly AppServices _ctx;
    private readonly Label _installed = new() { AutoSize = true };
    private readonly Label _latest = new() { AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.DimGray };
    private readonly Button _check = new() { Text = "Check now", AutoSize = true };
    private readonly Button _install = new() { Text = "Install the update", AutoSize = true, Enabled = false };
    private readonly Button _file = new() { Text = "Install from a file…", AutoSize = true };
    private UpdateInfo? _info;

    public UpdatesForm(AppServices ctx, UpdateInfo? known)
    {
        _ctx = ctx;
        Text = "Updates";
        Icon = AppIcon.Main;
        Width = 640;
        Height = 330;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        flow.Controls.Add(new Label { Text = "Inkspire version", Font = new Font("Segoe UI", 11f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
        flow.Controls.Add(_installed);
        flow.Controls.Add(_latest);
        flow.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _check, _install, _file });
        flow.Controls.Add(buttons);
        flow.Controls.Add(new Label
        {
            Text = "An update takes about a minute and stops Inkspire for everyone in that time. The database is backed up first and put back\r\nautomatically if anything goes wrong. Uploads and settings are never touched.",
            AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 16, 0, 0),
        });
        Controls.Add(flow);

        _check.Click += async (_, _) => await CheckAsync();
        _install.Click += (_, _) => InstallLatest();
        _file.Click += (_, _) => InstallFromFile();

        Show(known);
        Shown += async (_, _) => { if (known is null) await CheckAsync(); };
    }

    private void Show(UpdateInfo? info)
    {
        _info = info;
        _installed.Text = $"Installed:  {(string.IsNullOrEmpty(_ctx.Config.Versions.App) ? "unknown" : _ctx.Config.Versions.App)}";
        _latest.Text = $"Latest on GitHub:  {(info?.Latest is { } l ? l.Tag : "not checked yet")}";
        _status.ForeColor = info?.Error is not null ? Ui.Red : info?.Available == true ? Ui.Green : Color.DimGray;
        _status.Text = info switch
        {
            null => "",
            { Error: { } e } => e,
            { Available: true } => "A newer version is available.",
            _ => "You are up to date.",
        };
        _install.Enabled = info?.Available == true;
    }

    private async Task CheckAsync()
    {
        _check.Enabled = false;
        _status.ForeColor = Color.DimGray;
        _status.Text = "Checking GitHub…";
        var info = await UpdateChecker.CheckAsync(_ctx, CancellationToken.None);
        Show(info);
        PanelAppContext.Current?.SetUpdateInfo(info);
        _check.Enabled = true;
    }

    private bool Confirm() =>
        MessageBox.Show(this, "Inkspire will stop for about a minute for everyone on the network. The database is backed up first.\n\nContinue?",
            "Update Inkspire", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void InstallLatest()
    {
        if (_info?.Latest is not { } rel || !Confirm()) return;
        var ok = TaskDialog.Run(this, "Updating Inkspire", async (log, ct) =>
        {
            var sha = await GitHubReleases.ReadSha256Async(_ctx.Http, rel.Sha256Url, ct);
            var payload = new Payload("app", $"Inkspire {rel.Tag}", rel.AssetUrl, rel.AssetName, PayloadKind.Zip, rel.Tag, StripTopLevel: true, Sha256: sha, ExpectedSize: rel.Size);
            log($"Downloading {rel.AssetName} ({rel.Size / 1048576.0:0.0} MB)");
            var last = -1;
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.Fraction is not { } f) return;
                var pct = (int)(f * 100) / 10 * 10;
                if (pct != last) { last = pct; log($"  {pct}%"); }
            });
            var zip = await new DownloadManager(_ctx.Http, _ctx.Paths.DownloadsDir).FetchAsync(payload, progress, ct);
            await new AppUpdater(_ctx).ApplyAsync(zip, rel.Tag, log, ct);
        });
        AfterInstall(ok);
    }

    private void InstallFromFile()
    {
        using var dlg = new OpenFileDialog { Filter = "Inkspire app bundle (*.zip)|*.zip", Title = "Choose an Inkspire app bundle" };
        if (dlg.ShowDialog(this) != DialogResult.OK || !Confirm()) return;
        var version = Path.GetFileNameWithoutExtension(dlg.FileName).Replace("inkspire-app-", "", StringComparison.OrdinalIgnoreCase);
        var ok = TaskDialog.Run(this, "Updating Inkspire", (log, ct) => new AppUpdater(_ctx).ApplyAsync(dlg.FileName, version, log, ct));
        AfterInstall(ok);
    }

    private void AfterInstall(bool ok)
    {
        Show(_info);
        if (ok)
        {
            MessageBox.Show(this, "Inkspire is updated and running.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            PanelAppContext.Current?.SetUpdateInfo(null);
        }
        else
        {
            MessageBox.Show(this, "The update did not complete. If it got as far as switching versions, the previous version and its database were put back.\n\nThe details are in the window that just closed and in logs\\panel.log.",
                "Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
