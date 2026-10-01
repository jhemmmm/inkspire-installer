using System.Diagnostics;
using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Runtime;
using Inkspire.Panel.Setup;
using Inkspire.Panel.UI.Wizard.Pages;

namespace Inkspire.Panel.UI.Wizard;

/// <summary>The first-run setup wizard (also "Re-run setup"): a fixed list of pages, Back / Next / Cancel.</summary>
public sealed class WizardForm : Form, IWizardHost
{
    private readonly SingleInstance? _instance;
    private readonly WizardState _state;
    private readonly List<WizardPage> _pages = new();
    private readonly Label _title = new() { Font = new Font("Segoe UI", 14f, FontStyle.Bold), AutoSize = true, Left = 24, Top = 14 };
    private readonly Label _subtitle = new() { ForeColor = Color.DimGray, AutoSize = true, Left = 26, Top = 46, MaximumSize = new Size(700, 0) };
    private readonly System.Windows.Forms.Panel _header = new() { Dock = DockStyle.Top, Height = 78, BackColor = Color.White };
    private readonly System.Windows.Forms.Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Button _back = new() { Text = "Back", Width = 90, Height = 30 };
    private readonly Button _next = new() { Text = "Next", Width = 100, Height = 30 };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 90, Height = 30 };
    private readonly Label _error = new() { ForeColor = Ui.Red, AutoSize = true, MaximumSize = new Size(480, 0) };
    private CancellationTokenSource _cts = new();
    private int _index = -1;
    private bool _launchOnClose;
    private bool _finished;

    public WizardForm(CommandLine cli, SingleInstance? instance)
    {
        _instance = instance;
        _state = CreateState(cli);

        Text = "Inkspire Setup";
        Icon = AppIcon.Main;
        ClientSize = new Size(760, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _header.Controls.Add(_title);
        _header.Controls.Add(_subtitle);
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.Gainsboro);
            e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };

        var buttons = new System.Windows.Forms.Panel { Dock = DockStyle.Bottom, Height = 56 };
        _cancel.Left = 760 - 24 - _cancel.Width;
        _next.Left = _cancel.Left - 10 - _next.Width;
        _back.Left = _next.Left - 6 - _back.Width;
        _cancel.Top = _next.Top = _back.Top = 13;
        _error.Left = 24;
        _error.Top = 10;
        buttons.Controls.AddRange(new Control[] { _error, _back, _next, _cancel });

        Controls.Add(_content);
        Controls.Add(_header);
        Controls.Add(buttons);

        _back.Click += (_, _) => GoBack();
        _next.Click += (_, _) => NextClicked();
        _cancel.Click += (_, _) => CancelClicked();
        FormClosing += OnFormClosing;

        _pages.Add(new WelcomePage());
        _pages.Add(new ServerPage());
        _pages.Add(new CheckPage());
        _pages.Add(new AdminPage());
        _pages.Add(new IntegrationsPage());
        _pages.Add(new ReviewPage());
        _pages.Add(new DownloadPage());
        _pages.Add(new InstallPage());
        _pages.Add(new FinishPage());
        foreach (var p in _pages) p.Host = this;

        Show(0);
    }

    private static WizardState CreateState(CommandLine cli)
    {
        var root = cli.InstallRootOverride ?? InstallLocator.Find();
        if (root is not null)
        {
            try
            {
                var paths = new AppPaths(root);
                var config = new ConfigStore(paths.PanelConfigFile).Load();
                var env = File.Exists(paths.AppEnvFile) ? EnvFile.Load(paths.AppEnvFile) : null;
                return WizardState.FromExisting(config, env);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not pre-fill the wizard from the existing install: {ex.Message}");
            }
        }
        var state = new WizardState();
        if (cli.InstallRootOverride is not null) state.InstallRoot = cli.InstallRootOverride;
        if (cli.DevAppZip is not null) state.LocalAppZip = cli.DevAppZip;
        return state;
    }

    public CancellationToken CancelToken => _cts.Token;

    private void Show(int index)
    {
        if (_index >= 0) _pages[_index].OnLeave();
        _index = index;
        var page = _pages[index];
        _error.Text = "";
        _title.Text = page.Title;
        _subtitle.Text = page.Subtitle;
        _content.Controls.Clear();
        _content.Controls.Add(page);
        _next.Text = page.NextText;
        _next.Enabled = true;
        _back.Visible = page.CanGoBack && index > 0;
        _back.Enabled = true;
        _cancel.Visible = page.ShowCancel;
        _cancel.Enabled = true;
        AcceptButton = _next;
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        page.OnEnter(_state);
    }

    private void NextClicked()
    {
        var page = _pages[_index];
        if (_index == _pages.Count - 1)
        {
            Finish(launchPanel: (page as FinishPage)?.LaunchChecked ?? false);
            return;
        }
        var result = page.Validate(_state);
        if (!result.Ok)
        {
            _error.Text = result.Message ?? "Please check the entries.";
            return;
        }
        page.Save(_state);
        GoNext();
    }

    public void GoNext()
    {
        if (_index < _pages.Count - 1) Show(_index + 1);
    }

    public void GoBack()
    {
        if (_index > 0) Show(_index - 1);
    }

    public void SetNavigation(bool? back = null, bool? next = null, bool? cancel = null, string? nextText = null)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetNavigation(back, next, cancel, nextText)));
            return;
        }
        if (back is { } b)
        {
            _back.Enabled = b;
            if (b) _back.Visible = true; // a work page that failed offers Back even though it normally hides it
        }
        if (next is { } n) _next.Enabled = n;
        if (cancel is { } c) _cancel.Enabled = c;
        if (nextText is not null) _next.Text = nextText;
    }

    private void CancelClicked()
    {
        // On a work page the first press stops the work, it does not close the wizard.
        if (_pages[_index] is DownloadPage or InstallPage && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            return;
        }
        Close();
    }

    public void Finish(bool launchPanel)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Finish(launchPanel)));
            return;
        }
        _finished = true;
        _launchOnClose = launchPanel;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_finished || e.CloseReason != CloseReason.UserClosing) return;
        var onWork = _pages[_index] is InstallPage && !_state.InstallCompleted;
        var message = onWork
            ? "Setup is still working. Quit anyway? Nothing will be lost, you can run setup again."
            : "Quit setup? Inkspire will not be installed.";
        if (_pages[_index] is FinishPage) return;
        if (MessageBox.Show(this, message, "Inkspire Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _cts.Cancel();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _pages[_index].OnLeave();
        if (!_launchOnClose || !_state.InstallCompleted) return;

        try
        {
            // The new panel must be able to take the single-instance lock this process still holds.
            _instance?.Release();
            var panel = new AppPaths(_state.InstallRoot).PanelExe;
            Process.Start(new ProcessStartInfo(panel, "--show") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(panel) });
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the installed panel", ex);
            MessageBox.Show($"Setup finished, but the control panel could not be started:\n{ex.Message}\n\nStart it from the Start menu.",
                "Inkspire Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public WizardState State => _state;

    public int PageCount => _pages.Count;

    /// <summary>Developer harness: show page <paramref name="index"/>. Work pages (download, install) are shown empty so no
    /// real downloads or installs start.</summary>
    internal void DevShowPage(int index)
    {
        var page = _pages[index];
        if (page is DownloadPage or InstallPage)
        {
            _index = index;
            _title.Text = page.Title;
            _subtitle.Text = page.Subtitle;
            _content.Controls.Clear();
            _content.Controls.Add(page);
            return;
        }
        Show(index);
    }
}
