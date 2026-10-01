using System.Text;
using Inkspire.Panel.Network;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

public sealed class ReviewPage : WizardPage
{
    private readonly TextBox _text = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10f),
        BackColor = Color.White,
        ScrollBars = ScrollBars.Vertical,
    };

    public ReviewPage() => Controls.Add(_text);

    public override string Title => "Ready to install";
    public override string Subtitle => "Check the summary, then press Install.";
    public override string NextText => "Install";

    public override void OnEnter(WizardState s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Shop name           {s.ShopName}");
        sb.AppendLine($"Install folder      {s.InstallRoot}{(s.IsRepair ? "   (existing install: data and settings are kept)" : "")}");
        sb.AppendLine($"Web address         {s.AppUrl}");
        sb.AppendLine($"Other PCs           {(s.FirewallEnabled ? "allowed through the firewall: " + NetworkDiagnostics.DescribeScope(s.FirewallScope) : "firewall left alone")}");
        sb.AppendLine($"Administrator       {(s.Admin is null ? "(unchanged)" : s.AdminEmail)}");
        sb.AppendLine($"Email (Resend)      {(s.ResendApiKey.Length > 0 ? "configured" : "not set")}");
        sb.AppendLine($"Payments (PayMongo) {(s.PaymongoSecretKey.Length > 0 ? "configured" : "not set")}");
        sb.AppendLine($"Inkspire files      {(s.LocalAppZip is null ? "latest release from GitHub" : s.LocalAppZip)}");
        sb.AppendLine();
        sb.AppendLine("Next: download PHP, nginx and Inkspire (about 130 MB), then one Windows administrator");
        sb.AppendLine("prompt (firewall), install and set up the database. This takes a few minutes.");
        _text.Text = sb.ToString();
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;
    }
}
