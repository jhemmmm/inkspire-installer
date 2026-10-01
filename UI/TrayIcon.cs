using Inkspire.Panel.App;
using Inkspire.Panel.Network;
using Inkspire.Panel.Services;

namespace Inkspire.Panel.UI;

/// <summary>Notification-area icon: a coloured dot shows the overall state, the menu offers the common actions.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly PanelAppContext _app;
    private bool _balloonShown;

    public TrayIcon(PanelAppContext app)
    {
        _app = app;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open control panel", null, (_, _) => _app.ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start all services", null, (_, _) => _ = _app.StartAllAsync());
        menu.Items.Add("Stop all services", null, (_, _) => _ = _app.StopAllAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Inkspire in the browser", null, (_, _) => _app.OpenApp());
        menu.Items.Add("Copy network address", null, (_, _) => CopyNetworkAddress());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit (stops the server)", null, (_, _) => _ = _app.ExitAsync());

        _icon = new NotifyIcon
        {
            Icon = AppIcon.WithDot(Ui.Grey),
            Text = "Inkspire Control Panel",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _app.ShowMain();
    }

    public void RefreshState(ServiceManager services)
    {
        var enabled = services.Enabled.ToList();
        Color dot;
        string text;
        if (enabled.Any(s => s.Status.State == ServiceState.Failed))
        {
            dot = Ui.Red;
            text = "Inkspire: a service failed";
        }
        else if (enabled.Count > 0 && enabled.All(s => s.Status.State == ServiceState.Running))
        {
            dot = Ui.Green;
            text = $"Inkspire is running (port {_app.Ctx.Config.Web.Port})";
        }
        else if (enabled.Any(s => s.Status.IsActive))
        {
            dot = Ui.Amber;
            text = "Inkspire: starting or degraded";
        }
        else
        {
            dot = Ui.Grey;
            text = "Inkspire is stopped";
        }
        _icon.Icon = AppIcon.WithDot(dot);
        SetText(text);
    }

    public void SetText(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void ShowBalloon(string text) =>
        _icon.ShowBalloonTip(5000, "Inkspire Control Panel", text, ToolTipIcon.Info);

    public void ShowBalloonOnce(string text)
    {
        if (_balloonShown) return;
        _balloonShown = true;
        ShowBalloon(text);
    }

    private void CopyNetworkAddress()
    {
        var port = _app.Ctx.Config.Web.Port;
        var ip = LanAddresses.Primary()?.Ip;
        var url = ip is null ? $"http://localhost:{port}/" : $"http://{ip}:{port}/";
        if (Ui.TryCopy(url)) ShowBalloon($"Copied {url}");
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
