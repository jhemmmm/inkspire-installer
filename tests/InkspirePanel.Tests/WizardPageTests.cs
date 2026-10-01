using Inkspire.Panel.Setup;
using Inkspire.Panel.UI.Wizard.Pages;
using Xunit;

namespace InkspirePanel.Tests;

/// <summary>WinForms controls need a Windows-forms thread; each test runs its body on its own STA thread.</summary>
public class WizardPageTests
{
    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Test body failed: " + failure.Message, failure);
    }

    private static T Find<T>(Control page, string name) where T : Control => (T)page.Controls.Find(name, true).Single();

    [Fact]
    public void Server_page_rejects_an_empty_shop_name_and_a_port_inside_the_php_worker_range()
    {
        Sta(() =>
        {
            var state = new WizardState();
            var page = new ServerPage();
            page.OnEnter(state);

            Find<TextBox>(page, "shop").Text = "";
            Assert.False(page.Validate(state).Ok);

            Find<TextBox>(page, "shop").Text = "Squarefoot";
            Find<NumericUpDown>(page, "port").Value = 9003; // PHP workers use 9001-9006
            var result = page.Validate(state);
            Assert.False(result.Ok);
            Assert.Contains("9001-9006", result.Message);

            Find<NumericUpDown>(page, "port").Value = 8080;
            Assert.True(page.Validate(state).Ok);
        });
    }

    [Fact]
    public void Server_page_saves_its_inputs_into_the_wizard_state()
    {
        Sta(() =>
        {
            var state = new WizardState();
            var page = new ServerPage();
            page.OnEnter(state);
            Find<TextBox>(page, "shop").Text = "  Squarefoot  ";
            Find<TextBox>(page, "folder").Text = @"C:\Shop\Inkspire\";
            Find<NumericUpDown>(page, "port").Value = 8090;

            page.Save(state);

            Assert.Equal("Squarefoot", state.ShopName);
            Assert.Equal(@"C:\Shop\Inkspire", state.InstallRoot);
            Assert.Equal(8090, state.WebPort);
        });
    }

    [Fact]
    public void Admin_page_checks_email_password_policy_and_confirmation()
    {
        Sta(() =>
        {
            var state = new WizardState();
            var page = new AdminPage();
            page.OnEnter(state);
            Find<TextBox>(page, "name").Text = "Maria";
            Find<TextBox>(page, "email").Text = "maria@example.com";
            Find<TextBox>(page, "password").Text = "Str0ng!Passw0rd";
            Find<TextBox>(page, "confirm").Text = "Str0ng!Passw0rd";
            Assert.True(page.Validate(state).Ok);

            Find<TextBox>(page, "email").Text = "not-an-email";
            Assert.False(page.Validate(state).Ok);
            Find<TextBox>(page, "email").Text = "maria@example.com";

            Find<TextBox>(page, "password").Text = Find<TextBox>(page, "confirm").Text = "short1!";
            Assert.False(page.Validate(state).Ok);

            Find<TextBox>(page, "password").Text = "Str0ng!Passw0rd";
            Find<TextBox>(page, "confirm").Text = "Str0ng!Passw0rd-different";
            Assert.False(page.Validate(state).Ok);
        });
    }

    [Fact]
    public void Admin_page_save_puts_the_account_into_the_state_and_the_state_exposes_it_once_complete()
    {
        Sta(() =>
        {
            var state = new WizardState();
            Assert.Null(state.Admin);
            var page = new AdminPage();
            page.OnEnter(state);
            Find<TextBox>(page, "name").Text = " Maria ";
            Find<TextBox>(page, "email").Text = " maria@example.com ";
            Find<TextBox>(page, "password").Text = Find<TextBox>(page, "confirm").Text = "Str0ng!Passw0rd";

            page.Save(state);

            Assert.Equal("Maria", state.Admin!.Name);
            Assert.Equal("maria@example.com", state.Admin.Email);
        });
    }

    [Fact]
    public void Repairing_with_an_empty_password_keeps_the_existing_accounts()
    {
        Sta(() =>
        {
            var state = new WizardState { IsRepair = true, AdminEmail = "maria@example.com" };
            var page = new AdminPage();
            page.OnEnter(state);

            Assert.True(page.Validate(state).Ok);
            page.Save(state);
            Assert.Null(state.Admin); // no password: the install engine leaves the administrator alone
        });
    }

    [Fact]
    public void Integration_keys_must_look_like_the_real_thing_but_may_all_be_empty()
    {
        Sta(() =>
        {
            var state = new WizardState();
            var page = new IntegrationsPage();
            page.OnEnter(state);
            Assert.True(page.Validate(state).Ok);

            Find<TextBox>(page, "resend").Text = "abc123";
            Assert.False(page.Validate(state).Ok);
            Find<TextBox>(page, "resend").Text = "re_abc123";
            Assert.False(page.Validate(state).Ok); // a key without a sender address
            Find<TextBox>(page, "from").Text = "hello@shop.ph";
            Assert.True(page.Validate(state).Ok);

            Find<TextBox>(page, "pkSecret").Text = "pk_wrong_kind";
            Assert.False(page.Validate(state).Ok);
        });
    }

    [Fact]
    public void Integration_env_enables_resend_only_when_a_key_was_entered()
    {
        var empty = new WizardState();
        Assert.Empty(empty.IntegrationEnv());

        var s = new WizardState { ResendApiKey = "re_x", MailFromAddress = "hello@shop.ph", PaymongoSecretKey = "sk_live_abc", PaymongoPublicKey = "pk_live_abc" };
        var env = s.IntegrationEnv();
        Assert.Equal("resend", env["MAIL_MAILER"]);
        Assert.Equal("hello@shop.ph", env["MAIL_FROM_ADDRESS"]);
        Assert.Equal("true", env["PAYMONGO_LIVEMODE"]);
        Assert.False(env.ContainsKey("PAYMONGO_WEBHOOK_SIG")); // empty fields never overwrite a saved value
    }

    [Fact]
    public void Wizard_state_builds_the_config_and_the_app_url_from_its_inputs()
    {
        var s = new WizardState { ShopName = "Squarefoot", WebPort = 8090, LanIp = "192.168.1.50", InstallRoot = @"C:\Inkspire" };
        Assert.Equal("http://192.168.1.50:8090", s.AppUrl);
        var c = s.ToPanelConfig();
        Assert.Equal("Squarefoot", c.App.Name);
        Assert.Equal("http://192.168.1.50:8090", c.App.AppUrl);
        Assert.Equal(8090, c.Web.Port);
        Assert.False(c.App.Seeded);

        Assert.Equal("http://localhost:8090", new WizardState { WebPort = 8090 }.AppUrl);
    }
}
