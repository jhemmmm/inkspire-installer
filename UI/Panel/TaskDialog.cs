using Inkspire.Panel.App;

namespace Inkspire.Panel.UI.Panel;

/// <summary>A small modal progress window for long operations: live log, busy bar, Close when finished. On success it closes
/// by itself; on failure it stays open so the message can be read.</summary>
public sealed class TaskDialog : Form
{
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
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Height = 14 };
    private readonly Button _close = new() { Text = "Close", Enabled = false, Width = 90, Height = 28 };
    private readonly Func<Action<string>, CancellationToken, Task> _work;
    private readonly bool _autoClose;
    private readonly CancellationTokenSource _cts = new();
    private bool _success;

    private TaskDialog(string title, Func<Action<string>, CancellationToken, Task> work, bool autoClose)
    {
        _work = work;
        _autoClose = autoClose;
        Text = title;
        Icon = AppIcon.Main;
        Width = 720;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bottom.Controls.Add(_close);
        Controls.Add(_log);
        Controls.Add(_bar);
        Controls.Add(bottom);
        _close.Click += (_, _) => Close();
        Shown += async (_, _) => await RunAsync();
        FormClosing += (_, e) =>
        {
            if (_close.Enabled) return;
            e.Cancel = true; // still working: closing is not a way to abort half-way through
        };
    }

    /// <summary>Returns true when the work finished without an exception.</summary>
    public static bool Run(IWin32Window? owner, string title, Func<Action<string>, CancellationToken, Task> work, bool autoClose = true)
    {
        using var dlg = new TaskDialog(title, work, autoClose);
        dlg.ShowDialog(owner);
        return dlg._success;
    }

    private async Task RunAsync()
    {
        void Say(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(() => Say(line))); } catch (InvalidOperationException) { } return; }
            _log.AppendText(line + "\r\n");
        }

        try
        {
            await Task.Run(() => _work(Say, _cts.Token));
            _success = true;
            Say("Done.");
        }
        catch (Exception ex)
        {
            Log.Error($"{Text} failed", ex);
            Say("FAILED: " + ex.Message);
        }
        finally
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = _success ? _bar.Maximum : 0;
            _close.Enabled = true;
        }
        if (_success && _autoClose) Close();
    }
}
