using System.Text;
using Inkspire.Panel.Network;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

public sealed class FinishPage : WizardPage
{
    private readonly TextBox _summary = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Top,
        Height = 250,
        BackColor = Color.White,
        Font = new Font("Segoe UI", 10f),
        ScrollBars = ScrollBars.Vertical,
    };
    private readonly Button _copy = new() { Text = "Copy instructions for the other PCs", AutoSize = true };
    private readonly Button _shortcut = new() { Text = "Save an Inkspire.url shortcut…", AutoSize = true };
    private readonly CheckBox _launch = new() { Text = "Start Inkspire now (opens the control panel)", AutoSize = true, Checked = true };
    private WizardState? _state;

    public FinishPage()
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(_copy);
        buttons.Controls.Add(_shortcut);
        var launch = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        launch.Controls.Add(_launch);
        Controls.Add(launch);
        Controls.Add(buttons);
        Controls.Add(_summary);

        _copy.Click += (_, _) =>
        {
            if (_state is null) return;
            var text = NetworkDiagnostics.ClientInstructions(_state.ToPanelConfig(), Addresses());
            if (Ui.TryCopy(text)) MessageBox.Show(this, "Copied. Paste it into an email or a message for the other PCs' users.", "Inkspire", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _shortcut.Click += (_, _) =>
        {
            if (_state is null) return;
            using var dlg = new SaveFileDialog { FileName = "Inkspire.url", Filter = "Internet shortcut (*.url)|*.url", Title = "Save the shortcut for the other PCs" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dlg.FileName, NetworkDiagnostics.UrlShortcut(PrimaryUrl()));
        };
    }

    public bool LaunchChecked => _launch.Checked;

    public override string Title => "Inkspire is installed";
    public override string Subtitle => "One last look, then the control panel takes over.";
    public override string NextText => "Finish";
    public override bool CanGoBack => false;
    public override bool ShowCancel => false;

    private IReadOnlyList<LanAddress> Addresses() => LanAddresses.List();

    private string PrimaryUrl() => _state is { LanIp.Length: > 0 } s ? s.AppUrl + "/" : $"http://{Addresses().FirstOrDefault()?.Ip ?? "localhost"}:{_state?.WebPort ?? 8080}/";

    public override void OnEnter(WizardState s)
    {
        _state = s;
        var sb = new StringBuilder();
        sb.AppendLine("Inkspire is ready on this PC.");
        sb.AppendLine();
        sb.AppendLine($"On this PC:           http://localhost:{s.WebPort}/");
        if (s.FirewallEnabled && !s.FirewallFailed)
        {
            sb.AppendLine("On the other PCs, type one of these in the browser:");
            foreach (var a in Addresses()) sb.AppendLine($"                      http://{a.Ip}:{s.WebPort}/" + (a.IsDhcp ? "   (DHCP: may change)" : ""));
            sb.AppendLine($"                      http://{Environment.MachineName}:{s.WebPort}/   (this PC's name)");
        }
        else if (s.FirewallFailed)
        {
            sb.AppendLine();
            sb.AppendLine("⚠ The firewall rule could not be added, so the other PCs cannot connect yet.");
            sb.AppendLine("  Open the control panel > Network & clients > Fix, and approve the Windows prompt.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("The firewall was left alone. The other PCs can only connect if you allow");
            sb.AppendLine($"TCP port {s.WebPort} in Windows Firewall yourself.");
        }
        sb.AppendLine();
        sb.AppendLine("Remember:");
        sb.AppendLine("  •  Keep this PC on and logged in while the shop is open. The control panel starts");
        sb.AppendLine("     Inkspire automatically each time you sign in to Windows.");
        sb.AppendLine("  •  Sign in with the administrator account, then add each staff member under Admin > Users.");
        sb.AppendLine("  •  In your router, reserve this PC's address so it never changes.");
        _summary.Text = sb.ToString();
        _summary.SelectionStart = 0;
        _summary.SelectionLength = 0;
        Host?.SetNavigation(back: false, next: true, cancel: false);
    }
}
