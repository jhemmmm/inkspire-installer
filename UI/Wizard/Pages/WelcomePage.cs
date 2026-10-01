using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard.Pages;

public sealed class WelcomePage : WizardPage
{
    public override string Title => "Welcome to Inkspire setup";
    public override string Subtitle => "Run Inkspire on this PC so everyone in the shop can use it from their own computer.";
    public override bool CanGoBack => false;

    public WelcomePage()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.Controls.Add(FormRows.Body("This wizard installs everything Inkspire needs in one folder, then leaves a control panel in the notification area (next to the clock) that starts and stops it, like XAMPP."));
        flow.Controls.Add(FormRows.Body("What gets installed (about 130 MB):\r\n    •  Inkspire and its database (SQLite)\r\n    •  A web server (nginx) and PHP\r\n    •  This control panel"));
        flow.Controls.Add(FormRows.Body("What you need:\r\n    •  An internet connection for the downloads (or the files already in the downloads folder)\r\n    •  One click on a Windows \"administrator\" prompt: it opens the firewall so the other PCs can connect\r\n    •  This PC switched on and logged in while the shop is open"));
        flow.Controls.Add(FormRows.Body("The other computers need nothing installed. They only open a web address in their browser."));
        Controls.Add(flow);
    }

    public override void OnEnter(WizardState state)
    {
        if (state.IsRepair)
        {
            // Nothing to do on screen; the heading tells the story.
        }
    }
}
