using Inkspire.Panel.Config;
using Inkspire.Panel.Setup;
using Xunit;

namespace InkspirePanel.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inkspire-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

public class JunctionTests
{
    [Fact]
    public void A_junction_exposes_the_target_and_removing_it_keeps_the_target()
    {
        using var tmp = new TempDir();
        var target = tmp.Combine("data", "storage");
        var link = tmp.Combine("app", "storage");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "upload.pdf"), "important");

        Junction.Create(link, target);

        Assert.True(Junction.IsLink(link));
        Assert.True(Junction.PointsTo(link, target));
        Assert.Equal("important", File.ReadAllText(Path.Combine(link, "upload.pdf")));

        Junction.RemoveLink(link);

        Assert.False(Directory.Exists(link));
        Assert.Equal("important", File.ReadAllText(Path.Combine(target, "upload.pdf")));
    }

    [Fact]
    public void DeleteTree_removes_a_folder_holding_junctions_without_touching_their_targets()
    {
        using var tmp = new TempDir();
        var target = tmp.Combine("data");
        var app = tmp.Combine("app");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(app, "public"));
        File.WriteAllText(Path.Combine(app, "artisan"), "php");
        File.SetAttributes(Path.Combine(app, "artisan"), FileAttributes.ReadOnly); // vendor files often are
        File.WriteAllText(Path.Combine(target, "keep.txt"), "x");
        Junction.Create(Path.Combine(app, "storage"), target);
        Junction.Create(Path.Combine(app, "public", "storage"), target);

        // What the updater does with app.old and the uninstaller with app\. (Directory.Delete(recursive) throws here.)
        FolderOps.DeleteTree(app);

        Assert.False(Directory.Exists(app));
        Assert.Equal("x", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void DeleteTree_on_a_missing_folder_is_a_no_op()
    {
        using var tmp = new TempDir();
        FolderOps.DeleteTree(tmp.Combine("nope"));
    }

    [Fact]
    public void MoveContents_moves_a_junction_as_a_link_and_keeps_its_target()
    {
        using var tmp = new TempDir();
        var data = tmp.Combine("data");
        var from = tmp.Combine("app.new");
        var to = tmp.Combine("app");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(from);
        File.WriteAllText(Path.Combine(data, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(from, ".env"), "A=1");
        Junction.Create(Path.Combine(from, "storage"), data);

        FolderOps.MoveContents(from, to);

        Assert.True(Junction.PointsTo(Path.Combine(to, "storage"), data));
        Assert.Equal("x", File.ReadAllText(Path.Combine(to, "storage", "keep.txt")));
        Assert.Equal("A=1", File.ReadAllText(Path.Combine(to, ".env")));
    }

    [Fact]
    public void A_plain_folder_is_not_a_link()
    {
        using var tmp = new TempDir();
        Assert.False(Junction.IsLink(tmp.Path));
        Assert.Null(Junction.TargetOf(tmp.Path));
    }
}

public class ConfigStoreTests
{
    [Fact]
    public void Config_round_trips_including_enums()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Combine("config", "panel.json"));
        var config = new PanelConfig();
        config.Web.Port = 9090;
        config.Firewall.Scope = FirewallScope.PrivateRanges;
        config.App.Seeded = true;

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal(9090, loaded.Web.Port);
        Assert.Equal(FirewallScope.PrivateRanges, loaded.Firewall.Scope);
        Assert.True(loaded.App.Seeded);
        Assert.Contains("PrivateRanges", File.ReadAllText(store.Path)); // readable by humans
    }

    [Fact]
    public void A_config_from_a_newer_panel_is_refused()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("panel.json");
        File.WriteAllText(path, "{ \"SchemaVersion\": 999 }");
        Assert.Throws<InvalidDataException>(() => new ConfigStore(path).Load());
    }

    [Fact]
    public void Hand_edited_values_are_clamped_to_something_sane()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("panel.json");
        File.WriteAllText(path, "{ \"Web\": { \"Port\": 0, \"PoolSize\": 500, \"MaxUploadMb\": 1 } }");
        var c = new ConfigStore(path).Load();
        Assert.Equal(1, c.Web.Port);
        Assert.Equal(32, c.Web.PoolSize);
        Assert.Equal(8, c.Web.MaxUploadMb);
    }

    [Fact]
    public void Max_requests_zero_means_never_recycle_and_small_values_are_raised()
    {
        using var tmp = new TempDir();
        var path = tmp.Combine("panel.json");
        File.WriteAllText(path, "{ \"Web\": { \"MaxRequests\": 0 } }");
        Assert.Equal(0, new ConfigStore(path).Load().Web.MaxRequests);
        File.WriteAllText(path, "{ \"Web\": { \"MaxRequests\": 7 } }");
        Assert.Equal(50, new ConfigStore(path).Load().Web.MaxRequests);
        Assert.Equal(10000, new PanelConfig().Web.MaxRequests);
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Combine("panel.json"));
        store.Save(new PanelConfig());
        store.Save(new PanelConfig());
        Assert.Equal(new[] { "panel.json" }, Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }
}
