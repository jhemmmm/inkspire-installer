using Inkspire.Panel.App;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

/// <summary>Resolves the versions to install, then downloads whatever is not already in <c>downloads\</c>.</summary>
public sealed class DownloadPage : WizardPage
{
    private readonly FlowLayoutPanel _rows = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 12, 0, 0) };
    private readonly Button _retry = new() { Text = "Try again", AutoSize = true, Visible = false, Margin = new Padding(0, 8, 0, 0) };
    private WizardState? _state;
    private bool _running;

    public DownloadPage()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(_rows);
        flow.Controls.Add(_status);
        flow.Controls.Add(_retry);
        Controls.Add(flow);
        _retry.Click += (_, _) => Start();
    }

    public override string Title => "Downloading";
    public override string Subtitle => "Getting PHP, nginx and Inkspire. Files already in the downloads folder are reused.";
    public override bool CanGoBack => false;

    public override void OnEnter(WizardState state)
    {
        _state = state;
        Start();
    }

    private void Start()
    {
        if (_running || _state is null) return;
        _retry.Visible = false;
        Host?.SetNavigation(back: false, next: false, cancel: true);
        _ = RunAsync(_state);
    }

    private (Action<DownloadProgress> Update, Action<string> Done) AddRow(string title)
    {
        var name = new Label { Text = title, AutoSize = true, Width = 300 };
        var bar = new ProgressBar { Width = 330, Height = 16, Maximum = 1000 };
        var info = new Label { AutoSize = true, ForeColor = Color.DimGray, Width = 160 };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        row.Controls.Add(name);
        row.Controls.Add(bar);
        row.Controls.Add(info);
        _rows.Controls.Add(row);
        return (
            p =>
            {
                if (p.Fraction is { } f) bar.Value = (int)Math.Clamp(f * 1000, 0, 1000);
                info.Text = p.Total is { } t ? $"{p.Received / 1048576.0:0.0} / {t / 1048576.0:0.0} MB" : $"{p.Received / 1048576.0:0.0} MB";
            },
            text => { bar.Value = 1000; info.Text = text; });
    }

    private async Task RunAsync(WizardState s)
    {
        _running = true;
        var ct = Host!.CancelToken;
        _rows.Controls.Clear();
        _status.ForeColor = Color.DimGray;
        _status.Text = "Looking up the latest versions…";
        try
        {
            using var http = AppServices.CreateHttpClient();
            var paths = new AppPaths(s.InstallRoot);
            Directory.CreateDirectory(paths.DownloadsDir);

            s.Payloads.Clear();
            s.Downloaded.Clear();
            var list = await PayloadPlan.BuildAsync(s, http, ct);
            foreach (var p in list) s.Payloads[p.Id] = p;

            var downloads = new DownloadManager(http, paths.DownloadsDir);
            foreach (var p in list)
            {
                ct.ThrowIfCancellationRequested();
                var (update, done) = AddRow(p.Title);
                _status.Text = $"Downloading {p.Title}…";
                var wasCached = downloads.IsCached(p);
                var progress = new Progress<DownloadProgress>(update);
                s.Downloaded[p.Id] = await downloads.FetchAsync(p, progress, ct);
                done(wasCached ? "already downloaded" : "done");
            }

            _status.ForeColor = Ui.Green;
            _status.Text = "✔ Everything is downloaded.";
            _running = false;
            Host.GoNext();
        }
        catch (OperationCanceledException)
        {
            Fail("Cancelled. Press Try again to continue where it stopped.", Color.DimGray);
        }
        catch (NoAppReleaseException ex)
        {
            Fail(ex.Message + "\r\n\r\nGo back to \"Server settings\" and choose an app bundle (.zip).", Ui.Red, allowBack: true);
        }
        catch (Exception ex)
        {
            Log.Error("Download failed", ex);
            Fail("The download failed: " + ex.Message + "\r\n\r\nCheck the internet connection and try again. Partly downloaded files are resumed.", Ui.Red, allowBack: true);
        }
    }

    private void Fail(string message, Color color, bool allowBack = false)
    {
        _running = false;
        _status.ForeColor = color;
        _status.Text = message;
        _retry.Visible = true;
        Host?.SetNavigation(back: allowBack, next: false, cancel: true);
    }
}
