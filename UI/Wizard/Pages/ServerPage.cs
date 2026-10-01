using System.Net;
using Inkspire.Panel.Config;
using Inkspire.Panel.Network;
using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

public sealed class ServerPage : WizardPage
{
    private sealed record AddressChoice(string Ip, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly TextBox _shop = FormRows.Text(300);
    private readonly TextBox _folder = FormRows.Text(300);
    private readonly Button _browse = new() { Text = "Browse…", AutoSize = true };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Width = 90 };
    private readonly CheckBox _firewall = new() { Text = "Let the other PCs connect (adds a Windows Firewall rule)", AutoSize = true };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly ComboBox _address = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly Label _zipLabel = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(380, 0) };
    private readonly Button _zip = new() { Text = "Use an app bundle (.zip)…", AutoSize = true };
    private string? _localZip;

    public ServerPage()
    {
        _shop.Name = "shop";
        _folder.Name = "folder";
        _port.Name = "port";
        var table = FormRows.Table();

        FormRows.Add(table, "Shop name", _shop, "Shown in the browser tab and in emails.");

        var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        folderRow.Controls.Add(_folder);
        folderRow.Controls.Add(_browse);
        FormRows.Add(table, "Install folder", folderRow, "A plain folder on this PC, no network drive. Database, uploads and backups live in its data folder.");

        FormRows.Add(table, "Web port", _port, "The number after the address, e.g. http://192.168.1.50:8080. Port 80 is often taken by IIS or Skype, so 8080 is the default.");

        FormRows.Add(table, "Other PCs", _firewall);
        _scope.Items.AddRange(new object[]
        {
            "Only PCs on the same network (recommended)",
            "Any private address (10.x, 172.16-31.x, 192.168.x)",
            "Any address (not recommended)",
        });
        FormRows.Add(table, "Allowed from", _scope, "\"Same network\" is right when all PCs are on one router. Choose \"private\" if the shop has two routers or a wired and a Wi-Fi network that are separate.");

        FormRows.Add(table, "Address for the others", _address, "The address the other PCs will type. If it says DHCP, reserve it for this PC in the router so it never changes.");

        var zipRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        zipRow.Controls.Add(_zip);
        zipRow.Controls.Add(_zipLabel);
        FormRows.Add(table, "Inkspire files", zipRow, "Normally downloaded from GitHub (latest release). Pick a .zip only for an offline install or a test build.");

        Controls.Add(table);

        _browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Choose the Inkspire install folder", UseDescriptionForTitle = true, SelectedPath = _folder.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
        };
        _firewall.CheckedChanged += (_, _) => _scope.Enabled = _firewall.Checked;
        _zip.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Inkspire app bundle (*.zip)|*.zip", Title = "Choose an Inkspire app bundle" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _localZip = dlg.FileName;
            ShowZip();
        };
    }

    public override string Title => "Server settings";
    public override string Subtitle => "Where to install, and how the other computers will reach this one.";

    public override void OnEnter(WizardState s)
    {
        _shop.Text = s.ShopName;
        _folder.Text = s.InstallRoot;
        _port.Value = s.WebPort;
        _firewall.Checked = s.FirewallEnabled;
        _scope.SelectedIndex = (int)s.FirewallScope;
        _scope.Enabled = s.FirewallEnabled;
        _localZip = s.LocalAppZip;
        ShowZip();

        _address.Items.Clear();
        var addresses = LanAddresses.List();
        foreach (var a in addresses)
            _address.Items.Add(new AddressChoice(a.Ip, $"{a.Ip}   ({a.Adapter}{(a.IsDhcp ? ", DHCP: may change" : ", fixed")})"));
        _address.Items.Add(new AddressChoice("", "This PC only (no other PCs)"));

        var selected = _address.Items.Cast<AddressChoice>().ToList().FindIndex(c => c.Ip == s.LanIp && c.Ip.Length > 0);
        _address.SelectedIndex = selected >= 0 ? selected : 0;
    }

    private void ShowZip() => _zipLabel.Text = _localZip is null ? "latest release from GitHub" : Path.GetFileName(_localZip);

    public override ValidationResult Validate(WizardState s)
    {
        if (string.IsNullOrWhiteSpace(_shop.Text)) return ValidationResult.Fail("Enter the shop name.");
        if (string.IsNullOrWhiteSpace(_folder.Text)) return ValidationResult.Fail("Choose the install folder.");
        try { Path.GetFullPath(_folder.Text); }
        catch { return ValidationResult.Fail("That is not a valid folder path."); }

        var port = (int)_port.Value;
        if (port >= s.PoolBasePort && port < s.PoolBasePort + s.PoolSize)
            return ValidationResult.Fail($"Port {port} is used internally by PHP (ports {s.PoolBasePort}-{s.PoolBasePort + s.PoolSize - 1}). Choose another web port.");
        if (_localZip is not null && !File.Exists(_localZip)) return ValidationResult.Fail("The chosen app bundle no longer exists.");
        return ValidationResult.Pass;
    }

    public override void Save(WizardState s)
    {
        s.ShopName = _shop.Text.Trim();
        s.InstallRoot = Path.GetFullPath(_folder.Text.Trim()).TrimEnd('\\');
        s.WebPort = (int)_port.Value;
        s.FirewallEnabled = _firewall.Checked;
        s.FirewallScope = (FirewallScope)Math.Max(0, _scope.SelectedIndex);
        s.LanIp = (_address.SelectedItem as AddressChoice)?.Ip ?? "";
        s.LocalAppZip = _localZip;
        s.IsRepair = File.Exists(new Inkspire.Panel.App.AppPaths(s.InstallRoot).PanelConfigFile);
    }
}
