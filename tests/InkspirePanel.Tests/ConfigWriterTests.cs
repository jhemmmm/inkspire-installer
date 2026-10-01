using Inkspire.Panel.App;
using Inkspire.Panel.Config;
using Inkspire.Panel.Runtime;
using Xunit;

namespace InkspirePanel.Tests;

public class ConfigWriterTests
{
    private static readonly AppPaths Paths = new(@"C:\Inkspire");

    private static string Nginx(Action<PanelConfig>? tweak = null)
    {
        var config = new PanelConfig();
        tweak?.Invoke(config);
        return ConfigWriter.RenderNginxConf(config, Paths);
    }

    [Fact]
    public void Nginx_listens_on_all_interfaces_on_the_configured_port()
    {
        Assert.Contains("listen       0.0.0.0:8080;", Nginx());
        Assert.Contains("listen       0.0.0.0:9090;", Nginx(c => c.Web.Port = 9090));
    }

    [Fact]
    public void Nginx_has_one_upstream_server_per_pool_worker()
    {
        var conf = Nginx(c => { c.Web.PoolSize = 3; c.Web.PoolBasePort = 9100; });
        Assert.Contains("server 127.0.0.1:9100 ", conf);
        Assert.Contains("server 127.0.0.1:9101 ", conf);
        Assert.Contains("server 127.0.0.1:9102 ", conf);
        Assert.DoesNotContain("127.0.0.1:9103", conf);
    }

    [Fact]
    public void Nginx_balances_by_least_connections_so_requests_never_queue_behind_a_busy_worker()
    {
        Assert.Contains("least_conn;", Nginx());
    }

    [Fact]
    public void Nginx_uses_the_app_public_folder_with_forward_slashes()
    {
        Assert.Contains("root         \"C:/Inkspire/app/public\";", Nginx());
    }

    [Fact]
    public void Nginx_body_limit_follows_the_upload_setting()
    {
        Assert.Contains("client_max_body_size 256M;", Nginx());
        Assert.Contains("client_max_body_size 80M;", Nginx(c => c.Web.MaxUploadMb = 80));
    }

    [Fact]
    public void Nginx_does_not_shadow_laravels_signed_storage_urls()
    {
        var conf = Nginx();
        // Directives only: the template's own comments explain the rule and mention these words.
        var directives = string.Join("\n", conf.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        // Laravel serves the private "local" disk at /storage/{path}; a static location or alias there would 404 it.
        Assert.DoesNotContain("alias ", directives);
        Assert.DoesNotContain("location /storage", directives);
        Assert.DoesNotContain("location ~* \\.(", directives);
        Assert.Contains("try_files $uri $uri/ /index.php?$query_string;", directives);
        Assert.Contains("location ^~ /build/", directives);
    }

    [Fact]
    public void Nginx_only_runs_the_front_controller()
    {
        var conf = Nginx();
        Assert.Contains("location = /index.php", conf);
        Assert.Contains("location ~ \\.php$ {\n            return 404;", conf.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Nginx_template_has_no_unresolved_placeholders()
    {
        Assert.DoesNotContain("{{", Nginx());
    }

    [Fact]
    public void Env_keys_are_the_ones_the_panel_owns()
    {
        var env = ConfigWriter.BaseEnv(new PanelConfig(), Paths);
        Assert.Equal("production", env["APP_ENV"]);
        Assert.Equal("false", env["APP_DEBUG"]);
        Assert.Equal("sqlite", env["DB_CONNECTION"]);
        Assert.Equal("C:/Inkspire/data/inkspire.sqlite", env["DB_DATABASE"]);
        Assert.Equal("file", env["SESSION_DRIVER"]);
        Assert.Equal("file", env["CACHE_STORE"]);
        Assert.Equal("sync", env["QUEUE_CONNECTION"]);
        Assert.Equal("http://localhost:8080", env["APP_URL"]);
        Assert.False(env.ContainsKey("APP_KEY"));
    }

    [Fact]
    public void App_url_prefers_the_configured_lan_address()
    {
        var config = new PanelConfig();
        config.App.AppUrl = "http://192.168.1.50:8080/";
        Assert.Equal("http://192.168.1.50:8080", ConfigWriter.AppUrl(config));
    }

    [Fact]
    public void Php_ini_sets_limits_paths_and_enables_every_required_extension()
    {
        const string baseIni = "[PHP]\n;extension_dir = \"ext\"\nmemory_limit = 128M\n;extension=curl\n;extension=gd\n;extension=zip\n;zend_extension=opcache\nupload_max_filesize = 2M\n";
        var ini = ConfigWriter.BuildPhpIni(new PanelConfig(), Paths, baseIni).Build();

        Assert.Contains("extension_dir = \"C:/Inkspire/php/ext\"", ini);
        Assert.Contains("memory_limit = 512M", ini);
        Assert.Contains("upload_max_filesize = 256M", ini);
        Assert.Contains("post_max_size = 260M", ini);
        Assert.Contains("error_log = \"C:/Inkspire/logs/php-errors.log\"", ini);
        Assert.Contains("curl.cainfo = \"C:/Inkspire/php/cacert.pem\"", ini);
        Assert.Contains("sys_temp_dir = \"C:/Inkspire/data/tmp\"", ini);
        Assert.Contains("date.timezone = UTC", ini);
        foreach (var ext in ConfigWriter.PhpExtensions) Assert.Contains($"\nextension={ext}\n", ini);
        Assert.Contains("\nzend_extension=opcache\n", ini);
        Assert.DoesNotContain(";extension=curl", ini);
        Assert.DoesNotContain("memory_limit = 128M", ini);
    }

    [Fact]
    public void Missing_template_placeholder_is_an_error()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TemplateEngine.Render("a {{NOPE}} b", new Dictionary<string, string>()));
    }
}
