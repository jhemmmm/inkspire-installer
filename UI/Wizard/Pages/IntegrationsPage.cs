using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

/// <summary>Optional online services. Everything can stay empty and be added later with the panel's "Edit .env".</summary>
public sealed class IntegrationsPage : WizardPage
{
    private readonly TextBox _resend = FormRows.Text(360, password: true);
    private readonly TextBox _from = FormRows.Text();
    private readonly TextBox _pkPublic = FormRows.Text();
    private readonly TextBox _pkSecret = FormRows.Text(360, password: true);
    private readonly TextBox _pkWebhook = FormRows.Text(360, password: true);

    public IntegrationsPage()
    {
        _resend.Name = "resend";
        _from.Name = "from";
        _pkPublic.Name = "pkPublic";
        _pkSecret.Name = "pkSecret";
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.Controls.Add(FormRows.Body("Both are optional. Leave a section empty to skip it; Inkspire works without them (emails are written to its log instead of being sent)."));

        var mail = FormRows.Table();
        FormRows.Add(mail, "Resend API key", _resend, "For design-review and payment-reminder emails. Starts with re_ . Needs internet on this PC.");
        FormRows.Add(mail, "Send emails from", _from, "An address on a domain you verified in Resend, e.g. hello@yourshop.ph");
        flow.Controls.Add(new Label { Text = "Email (Resend)", Font = new Font("Segoe UI", 10f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 4, 0, 4) });
        flow.Controls.Add(mail);

        var pay = FormRows.Table();
        FormRows.Add(pay, "PayMongo public key", _pkPublic, "Starts with pk_test_ or pk_live_");
        FormRows.Add(pay, "PayMongo secret key", _pkSecret, "Starts with sk_test_ or sk_live_");
        FormRows.Add(pay, "Webhook secret", _pkWebhook, "Payments confirm automatically only if PayMongo can reach this PC from the internet. On a shop network the cashier confirms manually.");
        flow.Controls.Add(new Label { Text = "Online payments (PayMongo)", Font = new Font("Segoe UI", 10f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 12, 0, 4) });
        flow.Controls.Add(pay);
        Controls.Add(flow);
    }

    public override string Title => "Email and payments (optional)";
    public override string Subtitle => "You can skip this and add the keys later from the control panel.";

    public override void OnEnter(WizardState s)
    {
        _resend.Text = s.ResendApiKey;
        _from.Text = s.MailFromAddress;
        _pkPublic.Text = s.PaymongoPublicKey;
        _pkSecret.Text = s.PaymongoSecretKey;
        _pkWebhook.Text = s.PaymongoWebhookSignature;
    }

    public override ValidationResult Validate(WizardState s)
    {
        if (_resend.Text.Trim().Length > 0 && !_resend.Text.Trim().StartsWith("re_", StringComparison.Ordinal))
            return ValidationResult.Fail("A Resend API key starts with re_ . Leave it empty to skip.");
        if (_resend.Text.Trim().Length > 0 && !_from.Text.Contains('@'))
            return ValidationResult.Fail("Enter the address emails should be sent from.");
        if (_pkPublic.Text.Trim().Length > 0 && !_pkPublic.Text.Trim().StartsWith("pk_", StringComparison.Ordinal))
            return ValidationResult.Fail("A PayMongo public key starts with pk_ .");
        if (_pkSecret.Text.Trim().Length > 0 && !_pkSecret.Text.Trim().StartsWith("sk_", StringComparison.Ordinal))
            return ValidationResult.Fail("A PayMongo secret key starts with sk_ .");
        return ValidationResult.Pass;
    }

    public override void Save(WizardState s)
    {
        s.ResendApiKey = _resend.Text.Trim();
        s.MailFromAddress = _from.Text.Trim();
        s.PaymongoPublicKey = _pkPublic.Text.Trim();
        s.PaymongoSecretKey = _pkSecret.Text.Trim();
        s.PaymongoWebhookSignature = _pkWebhook.Text.Trim();
    }
}
