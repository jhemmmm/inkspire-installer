using Inkspire.Panel.App;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

public sealed class InstallPage : WizardPage
{
    private readonly ListView _steps = new() { View = View.Details, HeaderStyle = ColumnHeaderStyle.None, Dock = DockStyle.Top, Height = 190, BorderStyle = BorderStyle.FixedSingle };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 14, Maximum = 1000 };
    private readonly Label _detail = new() { AutoSize = true, ForeColor = Color.DimGray, Dock = DockStyle.Top, Padding = new Padding(0, 4, 0, 4) };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f),
        BackColor = Color.FromArgb(24, 24, 28),
        ForeColor = Color.Gainsboro,
    };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(680, 0), Dock = DockStyle.Bottom };
    private string? _current;
    private bool _running;

    public InstallPage()
    {
        _steps.Columns.Add("", 24);
        _steps.Columns.Add("", 640);
        Controls.Add(_log);
        Controls.Add(_result);
        Controls.Add(_detail);
        Controls.Add(_bar);
        Controls.Add(_steps);
    }

    public override string Title => "Installing";
    public override string Subtitle => "Please keep this window open. Windows will ask for administrator approval once.";
    public override bool CanGoBack => false;

    public override void OnEnter(WizardState state)
    {
        if (_running) return;
        _steps.Items.Clear();
        _log.Clear();
        _result.Text = "";
        _current = null;
        _bar.Value = 0;
        Host?.SetNavigation(back: false, next: false, cancel: true);
        _ = RunAsync(state);
    }

    private async Task RunAsync(WizardState s)
    {
        _running = true;
        var ct = Host!.CancelToken;
        var engine = new InstallEngine(s, new Progress<StepProgress>(OnProgress), AppendLog);
        try
        {
            await Task.Run(() => engine.RunAsync(ct), ct);
            MarkCurrentDone();
            _bar.Value = 1000;
            s.InstallCompleted = true;
            _running = false;
            Host.GoNext();
        }
        catch (OperationCanceledException ex)
        {
            Fail(string.IsNullOrWhiteSpace(ex.Message) || ex.Message.StartsWith("The operation") ? "Cancelled." : ex.Message, s);
        }
        catch (Exception ex)
        {
            Log.Error("Install failed", ex);
            AppendLog("FAILED: " + ex.Message);
            Fail("Setup stopped: " + ex.Message, s);
        }
    }

    private void Fail(string message, WizardState s)
    {
        _running = false;
        s.InstallError = message;
        _result.ForeColor = Ui.Red;
        _result.Text = message + "\r\n\r\nNothing already installed is lost. Go Back and press Install again after fixing the problem; the log is in the install folder under logs\\panel.log.";
        Host?.SetNavigation(back: true, next: false, cancel: true);
    }

    private void OnProgress(StepProgress p)
    {
        if (p.Step == "Finished") return;
        if (p.Step != _current)
        {
            MarkCurrentDone();
            _current = p.Step;
            var item = new ListViewItem("▶") { ForeColor = Ui.Amber };
            item.SubItems.Add(p.Step);
            _steps.Items.Add(item);
            item.EnsureVisible();
        }
        if (p.Fraction is { } f) _bar.Value = (int)Math.Clamp(f * 1000, 0, 1000);
        _detail.Text = p.Message;
    }

    private void MarkCurrentDone()
    {
        if (_steps.Items.Count == 0) return;
        var last = _steps.Items[^1];
        last.Text = "✔";
        last.ForeColor = Ui.Green;
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => AppendLog(line))); }
            catch (InvalidOperationException) { /* window already gone */ }
            return;
        }
        _log.AppendText(line + "\r\n");
    }
}
