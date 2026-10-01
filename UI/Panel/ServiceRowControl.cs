using Inkspire.Panel.App;
using Inkspire.Panel.Services;

namespace Inkspire.Panel.UI.Panel;

/// <summary>One XAMPP-style row: status dot, name, port, pids, uptime, Start/Stop, Restart, Logs.</summary>
public sealed class ServiceRowControl : UserControl
{
    private readonly ServiceBase _service;
    private readonly System.Windows.Forms.Panel _dot = new() { Width = 20, Height = 20, Anchor = AnchorStyles.None };
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _detail = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 4) };
    private readonly Label _port = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _pids = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _uptime = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _toggle = new() { Text = "Start", Width = 78, Height = 28 };
    private readonly Button _restart = new() { Text = "Restart", Width = 78, Height = 28 };
    private readonly Button _logs = new() { Text = "Logs", Width = 78, Height = 28 };
    private Color _dotColor = Ui.Grey;

    public event Action<ServiceBase>? LogsRequested;

    public ServiceRowControl(ServiceBase service)
    {
        _service = service;
        Dock = DockStyle.Top;
        Height = 60;
        BackColor = Color.White;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Padding = new Padding(8, 4, 8, 4),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _dot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_dotColor);
            e.Graphics.FillEllipse(brush, 2, 2, 16, 16);
        };

        var names = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        _title.Text = service.Title;
        names.Controls.Add(_title);
        names.Controls.Add(_detail);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.Add(_restart);
        buttons.Controls.Add(_logs);

        layout.Controls.Add(_dot, 0, 0);
        layout.Controls.Add(names, 1, 0);
        layout.Controls.Add(_port, 2, 0);
        layout.Controls.Add(_pids, 3, 0);
        layout.Controls.Add(_uptime, 4, 0);
        layout.Controls.Add(_toggle, 5, 0);
        layout.Controls.Add(buttons, 6, 0);
        Controls.Add(layout);

        _toggle.Click += (_, _) => _ = _service.Status.IsActive ? _service.StopAsync() : _service.StartAsync();
        _restart.Click += (_, _) => _ = _service.RestartAsync();
        _logs.Click += (_, _) => LogsRequested?.Invoke(_service);

        _service.StatusChanged += _ => UiThread.Post(() => { if (!IsDisposed) Refresh(); });
        Refresh();
    }

    /// <summary>Called once a second for the live parts (uptime, restart countdown).</summary>
    public void Tick()
    {
        if (!IsDisposed) Refresh();
    }

    public override void Refresh()
    {
        var status = _service.Status;
        _dotColor = Ui.StateColor(status.State);
        _dot.Invalidate();

        _port.Text = _service.PortLabel;
        _pids.Text = status.Pids.Count == 0 ? "" : "PID " + string.Join(", ", status.Pids.Take(3)) + (status.Pids.Count > 3 ? "…" : "");
        _uptime.Text = status.StartedAtUtc is { } started && status.IsServing
            ? $"{Ui.StateText(status)}  {Ui.FormatUptime(DateTime.UtcNow - started)}"
            : Ui.StateText(status);

        var bad = status.State is ServiceState.Failed or ServiceState.Degraded or ServiceState.WaitingRestart;
        _detail.ForeColor = bad ? Ui.Red : Color.DimGray;
        _detail.Text = string.IsNullOrWhiteSpace(status.Detail) ? _service.Description : Truncate(status.Detail!, 110);

        _toggle.Text = status.IsActive ? "Stop" : "Start";
        _toggle.Enabled = status.State is not (ServiceState.Starting or ServiceState.Stopping);
        _restart.Enabled = status.IsActive && status.State is not (ServiceState.Starting or ServiceState.Stopping);
        base.Refresh();
    }

    private static string Truncate(string s, int max)
    {
        var oneLine = s.ReplaceLineEndings(" ");
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 1)] + "…";
    }
}
