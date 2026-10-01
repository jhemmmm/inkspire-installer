using Inkspire.Panel.App;
using Inkspire.Panel.Network;
using Inkspire.Panel.Processes;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

public sealed class CheckPage : WizardPage
{
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.None,
        Dock = DockStyle.Top,
        Height = 330,
        BorderStyle = BorderStyle.FixedSingle,
        ShowItemToolTips = true,
    };
    private readonly Button _again = new() { Text = "Check again", AutoSize = true, Top = 340, Left = 0 };
    private readonly Label _summary = new() { AutoSize = true, Top = 346, Left = 120, MaximumSize = new Size(560, 0) };
    private WizardState? _state;
    private int _generation;

    public CheckPage()
    {
        _list.Columns.Add("", 30);
        _list.Columns.Add("Check", 200);
        _list.Columns.Add("Result", 470);

        var holder = new System.Windows.Forms.Panel { Dock = DockStyle.Fill };
        holder.Controls.Add(_again);
        holder.Controls.Add(_summary);
        Controls.Add(holder);
        Controls.Add(_list);
        _again.Click += (_, _) => Run();
    }

    public override string Title => "System check";
    public override string Subtitle => "Making sure this PC is ready. Red items must be fixed first (use Back to change the folder or port).";

    public override void OnEnter(WizardState state)
    {
        _state = state;
        Run();
    }

    private void Run()
    {
        if (_state is null) return;
        var s = _state;
        var generation = ++_generation;
        _list.Items.Clear();
        _summary.Text = "Checking…";
        Host?.SetNavigation(next: false);

        // A previous crashed run may have left nginx/php holding the ports under this very folder.
        s.IsRepair = File.Exists(new AppPaths(s.InstallRoot).PanelConfigFile);
        if (s.IsRepair) ProcessSweep.Sweep(s.InstallRoot);

        var results = PrereqChecker.CheckSystem(s).ToList();
        Render(results);

        // The internet check takes a moment: show the rest first.
        _ = Task.Run(async () =>
        {
            using var http = AppServices.CreateHttpClient();
            var online = await PrereqChecker.HasInternetAsync(http, CancellationToken.None).ConfigureAwait(false);
            var cached = s.LocalAppZip is not null || Directory.Exists(new AppPaths(s.InstallRoot).DownloadsDir);
            var extra = online
                ? new CheckResult("Internet", CheckLevel.Ok, "reachable (GitHub)")
                : new CheckResult("Internet", cached ? CheckLevel.Warning : CheckLevel.Blocker,
                    "not reachable. Connect this PC to the internet, or put the downloads in the install folder's downloads\\ folder and choose an app bundle on the previous page.");
            BeginInvoke(new Action(() =>
            {
                if (generation != _generation || IsDisposed) return;
                results.Add(extra);
                Render(results);
            }));
        });
    }

    private void Render(IReadOnlyList<CheckResult> results)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
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
            _list.Items.Add(item);
        }
        _list.EndUpdate();

        var blockers = results.Count(r => r.Level == CheckLevel.Blocker);
        var warnings = results.Count(r => r.Level == CheckLevel.Warning);
        _summary.ForeColor = blockers > 0 ? Ui.Red : Color.DimGray;
        _summary.Text = blockers > 0
            ? $"{blockers} problem(s) to fix. Use Back, change the folder or port, then Check again."
            : warnings > 0 ? $"Ready, with {warnings} thing(s) worth a look." : "Everything looks good.";
        Host?.SetNavigation(next: blockers == 0);
    }

    public override void OnLeave() => _generation++;
}
