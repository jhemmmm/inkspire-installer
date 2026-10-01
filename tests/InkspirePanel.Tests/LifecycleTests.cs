using Inkspire.Panel.Config;
using Inkspire.Panel.Network;
using Inkspire.Panel.Runtime;
using Xunit;

namespace InkspirePanel.Tests;

public class LogRotationTests
{
    [Fact]
    public void A_big_log_is_shifted_and_a_small_one_is_left_alone()
    {
        using var tmp = new TempDir();
        var log = tmp.Combine("access.log");
        File.WriteAllText(log, new string('x', 100));
        Assert.False(LogRotation.RotateIfLarge(log, maxBytes: 1000));
        Assert.True(File.Exists(log));

        Assert.True(LogRotation.RotateIfLarge(log, maxBytes: 50));
        Assert.False(File.Exists(log));
        Assert.True(File.Exists(log + ".1"));
    }

    [Fact]
    public void Rotation_keeps_only_the_configured_number_of_generations()
    {
        using var tmp = new TempDir();
        var log = tmp.Combine("error.log");
        for (var generation = 1; generation <= 6; generation++)
        {
            File.WriteAllText(log, $"generation {generation} " + new string('x', 60));
            LogRotation.RotateIfLarge(log, maxBytes: 50, keep: 3);
        }
        var files = Directory.GetFiles(tmp.Path).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "error.log.1", "error.log.2", "error.log.3" }, files);
        Assert.StartsWith("generation 6", File.ReadAllText(log + ".1"));
        Assert.StartsWith("generation 4", File.ReadAllText(log + ".3"));
    }

    [Fact]
    public void A_missing_log_is_not_an_error()
    {
        using var tmp = new TempDir();
        Assert.False(LogRotation.RotateIfLarge(tmp.Combine("nope.log")));
    }
}

public class SettingsDiffTests
{
    private static (PanelConfig Before, PanelConfig After) Pair(Action<PanelConfig> change)
    {
        var before = new PanelConfig();
        var after = ConfigStore.Clone(before);
        change(after);
        return (before, after);
    }

    [Fact]
    public void Clone_is_deep()
    {
        var a = new PanelConfig();
        var b = ConfigStore.Clone(a);
        b.Web.Port = 9999;
        b.Firewall.Scope = FirewallScope.Any;
        Assert.Equal(8080, a.Web.Port);
        Assert.Equal(FirewallScope.LocalSubnet, a.Firewall.Scope);
    }

    [Fact]
    public void Only_changes_that_need_a_restart_restart_the_server()
    {
        var (b, a) = Pair(c => c.Ui.KeepAwake = false);
        Assert.False(SettingsApplier.NeedsServerRestart(b, a));
        Assert.False(SettingsApplier.FirewallChanged(b, a));

        (b, a) = Pair(c => c.Web.PoolSize = 8);
        Assert.True(SettingsApplier.NeedsServerRestart(b, a));
        Assert.False(SettingsApplier.FirewallChanged(b, a)); // workers are loopback-only: no firewall change

        (b, a) = Pair(c => c.Web.MaxUploadMb = 512);
        Assert.True(SettingsApplier.NeedsServerRestart(b, a));
    }

    [Fact]
    public void A_port_change_touches_the_server_the_env_and_the_firewall()
    {
        var (b, a) = Pair(c => c.Web.Port = 9090);
        Assert.True(SettingsApplier.NeedsServerRestart(b, a));
        Assert.True(SettingsApplier.NeedsEnvRewrite(b, a));
        Assert.True(SettingsApplier.FirewallChanged(b, a));
    }

    [Fact]
    public void Scope_and_enable_changes_only_touch_the_firewall()
    {
        var (b, a) = Pair(c => c.Firewall.Scope = FirewallScope.PrivateRanges);
        Assert.True(SettingsApplier.FirewallChanged(b, a));
        Assert.False(SettingsApplier.NeedsServerRestart(b, a));

        (b, a) = Pair(c => c.Firewall.Enabled = false);
        Assert.True(SettingsApplier.FirewallChanged(b, a));
    }

    [Fact]
    public void Shop_name_and_address_rewrite_the_env_without_restarting_nginx()
    {
        var (b, a) = Pair(c => c.App.Name = "Squarefoot");
        Assert.True(SettingsApplier.NeedsEnvRewrite(b, a));
        Assert.False(SettingsApplier.NeedsServerRestart(b, a));

        (b, a) = Pair(c => c.App.AppUrl = "http://192.168.1.60:8080");
        Assert.True(SettingsApplier.NeedsEnvRewrite(b, a));
    }
}

public class ClientInstructionsTests
{
    [Fact]
    public void Instructions_name_every_address_the_test_command_and_the_one_account_per_person_rule()
    {
        var config = new PanelConfig { Web = { Port = 8080 }, App = { Name = "Squarefoot" } };
        var addresses = new[]
        {
            new LanAddress("192.168.1.50", "Ethernet", false, true, "192.168.1.0/24", System.Net.NetworkInformation.NetworkInterfaceType.Ethernet),
            new LanAddress("192.168.1.51", "Wi-Fi", true, false, "192.168.1.0/24", System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211),
        };

        var text = NetworkDiagnostics.ClientInstructions(config, addresses);

        Assert.Contains("http://192.168.1.50:8080/", text);
        Assert.Contains("http://192.168.1.51:8080/", text);
        Assert.Contains($"http://{Environment.MachineName}:8080/", text);
        Assert.Contains("Test-NetConnection 192.168.1.50 -Port 8080", text);
        Assert.Contains("own account", text);
        Assert.Contains("Squarefoot", text);
    }

    [Fact]
    public void The_url_shortcut_is_a_valid_internet_shortcut()
    {
        Assert.Equal("[InternetShortcut]\r\nURL=http://192.168.1.50:8080/\r\n", NetworkDiagnostics.UrlShortcut("http://192.168.1.50:8080/"));
    }
}

public class RestoreDatabaseTests
{
    [Fact]
    public void Restoring_removes_the_wal_and_shm_files_of_the_old_database()
    {
        using var tmp = new TempDir();
        var db = tmp.Combine("inkspire.sqlite");
        File.WriteAllText(db, "OLD");
        File.WriteAllText(db + "-wal", "stale wal");
        File.WriteAllText(db + "-shm", "stale shm");
        var backup = tmp.Combine("backup.sqlite");
        File.WriteAllText(backup, "SNAPSHOT");

        SqliteBackup.Restore(db, backup);

        Assert.Equal("SNAPSHOT", File.ReadAllText(db));
        Assert.False(File.Exists(db + "-wal"));
        Assert.False(File.Exists(db + "-shm"));
        Assert.Equal("SNAPSHOT", File.ReadAllText(backup)); // the backup itself is untouched
    }
}

public class RegistrationTests
{
    [Fact]
    public void Run_at_login_writes_the_autostart_command_and_removing_it_deletes_the_value()
    {
        var name = "InkspirePanel.Test." + Guid.NewGuid().ToString("N")[..8]; // never the real entry's name
        var paths = new Inkspire.Panel.App.AppPaths(@"C:\Shop\Inkspire");
        try
        {
            Assert.False(Inkspire.Panel.App.StartupRegistration.IsEnabled(name));

            Inkspire.Panel.App.StartupRegistration.Apply(true, paths, name);
            Assert.True(Inkspire.Panel.App.StartupRegistration.IsEnabled(name));
            Assert.Equal(@"""C:\Shop\Inkspire\panel\InkspirePanel.exe"" --autostart", Inkspire.Panel.App.StartupRegistration.CurrentCommand(name));

            Inkspire.Panel.App.StartupRegistration.Apply(false, paths, name);
            Assert.False(Inkspire.Panel.App.StartupRegistration.IsEnabled(name));
        }
        finally
        {
            Inkspire.Panel.App.StartupRegistration.Apply(false, paths, name);
        }
    }

    [Fact]
    public void The_install_pointer_is_found_only_while_the_folder_still_holds_a_panel_config()
    {
        using var tmp = new TempDir();
        var root = tmp.Combine("Inkspire");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Inkspire.Panel.App.InstallLocator.PointerFileOverride = tmp.Combine("pointer", "install.json");
        try
        {
            Assert.Null(Inkspire.Panel.App.InstallLocator.Find());

            Inkspire.Panel.App.InstallLocator.WritePointer(root);
            Assert.Null(Inkspire.Panel.App.InstallLocator.Find()); // no panel.json yet: not an install

            File.WriteAllText(Path.Combine(root, "config", "panel.json"), "{}");
            Assert.Equal(root, Inkspire.Panel.App.InstallLocator.Find());

            Inkspire.Panel.App.InstallLocator.DeletePointer();
            Assert.Null(Inkspire.Panel.App.InstallLocator.Find());
        }
        finally
        {
            Inkspire.Panel.App.InstallLocator.PointerFileOverride = null;
        }
    }

    [Fact]
    public void The_start_menu_shortcut_points_at_the_panel_and_asks_for_the_window()
    {
        using var tmp = new TempDir();
        var paths = new Inkspire.Panel.App.AppPaths(@"C:\Shop\Inkspire");
        var link = tmp.Combine("Inkspire Control Panel.lnk");

        Inkspire.Panel.Setup.Shortcuts.Create(link, paths);

        Assert.True(File.Exists(link));
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic saved = ((dynamic)shell).CreateShortcut(link);
        Assert.Equal(@"C:\Shop\Inkspire\panel\InkspirePanel.exe", (string)saved.TargetPath);
        Assert.Equal("--show", (string)saved.Arguments);

        Inkspire.Panel.Setup.Shortcuts.Delete(link);
        Assert.False(File.Exists(link));
    }
}
