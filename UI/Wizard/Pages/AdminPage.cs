using System.Text.RegularExpressions;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

public sealed class AdminPage : WizardPage
{
    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly TextBox _name = FormRows.Text();
    private readonly TextBox _email = FormRows.Text();
    private readonly TextBox _password = FormRows.Text(360, password: true);
    private readonly TextBox _confirm = FormRows.Text(360, password: true);
    private readonly CheckBox _show = new() { Text = "Show password", AutoSize = true };
    private readonly Label _strength = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _repairNote = new() { AutoSize = true, ForeColor = Color.SteelBlue, MaximumSize = new Size(620, 0), Visible = false };
    private bool _repair;

    public AdminPage()
    {
        _name.Name = "name";
        _email.Name = "email";
        _password.Name = "password";
        _confirm.Name = "confirm";
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(_repairNote);

        var table = FormRows.Table();
        FormRows.Add(table, "Your name", _name, "Shown in the audit trail.");
        FormRows.Add(table, "Email (login)", _email, "You sign in to Inkspire with this address.");
        FormRows.Add(table, "Password", _password, "At least 12 characters with upper and lower case letters, a number and a symbol.");
        FormRows.Add(table, "Repeat password", _confirm);
        FormRows.Add(table, "", _show);
        flow.Controls.Add(table);
        flow.Controls.Add(_strength);
        flow.Controls.Add(FormRows.Body("\r\nThis becomes the Administrator account. The staff accounts (cashier, artist, ...) are added later inside Inkspire under Admin > Users, one per person."));
        Controls.Add(flow);

        _show.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = _confirm.UseSystemPasswordChar = !_show.Checked;
        _password.TextChanged += (_, _) => UpdateStrength();
    }

    public override string Title => "Administrator account";
    public override string Subtitle => "The first login. You can add everyone else from inside Inkspire.";

    public override void OnEnter(WizardState s)
    {
        _repair = s.IsRepair;
        _repairNote.Visible = _repair;
        _repairNote.Text = "Inkspire is already installed here. Leave the password empty to keep the current accounts; fill it in to set the administrator's password again.";
        if (_name.Text.Length == 0) _name.Text = s.AdminName;
        if (_email.Text.Length == 0) _email.Text = s.AdminEmail;
        UpdateStrength();
    }

    private void UpdateStrength()
    {
        if (_password.Text.Length == 0)
        {
            _strength.Text = "";
            return;
        }
        var problem = PasswordPolicy.Validate(_password.Text);
        _strength.ForeColor = problem is null ? Ui.Green : Ui.Amber;
        _strength.Text = problem ?? "✔ Good password.";
    }

    public override ValidationResult Validate(WizardState s)
    {
        var keepExisting = _repair && _password.Text.Length == 0 && _confirm.Text.Length == 0;
        if (keepExisting) return ValidationResult.Pass;

        if (_name.Text.Trim().Length == 0) return ValidationResult.Fail("Enter your name.");
        if (!Email.IsMatch(_email.Text.Trim())) return ValidationResult.Fail("Enter a valid email address.");
        if (PasswordPolicy.Validate(_password.Text) is { } problem) return ValidationResult.Fail(problem);
        if (_password.Text != _confirm.Text) return ValidationResult.Fail("The two passwords are not the same.");
        return ValidationResult.Pass;
    }

    public override void Save(WizardState s)
    {
        s.AdminName = _name.Text.Trim();
        s.AdminEmail = _email.Text.Trim();
        s.AdminPassword = _password.Text;
    }
}
