using Inkspire.Panel.Runtime;
using Inkspire.Panel.Setup;
using Xunit;

namespace InkspirePanel.Tests;

public class PayloadTests
{
    // Shape copied from the live https://windows.php.net/downloads/releases/releases.json: "size" is a STRING there.
    private const string PhpReleases = """
        {
          "8.4": {
            "version": "8.4.26",
            "nts-vs17-x86": { "zip": { "path": "php-8.4.26-nts-Win32-vs17-x86.zip", "size": "31MB", "sha256": "aaaa" } },
            "ts-vs17-x64": { "zip": { "path": "php-8.4.26-Win32-vs17-x64.zip", "size": "33MB", "sha256": "bbbb" } },
            "nts-vs17-x64": {
              "mtime": "2026-09-22T15:11:32+00:00",
              "zip": { "path": "php-8.4.26-nts-Win32-vs17-x64.zip", "size": "33.48MB", "sha256": "da68394f9193b7f6b89d0c76861a4034ae10efee7fd55a7255d8118c2acf70d7" },
              "debug_pack": { "path": "php-debug-pack-8.4.26-nts-Win32-vs17-x64.zip", "size": "20MB", "sha256": "cccc" }
            }
          },
          "8.3": { "version": "8.3.30" }
        }
        """;

    [Fact]
    public void Php_resolution_picks_the_nts_x64_zip_of_the_84_series_and_keeps_its_checksum()
    {
        var p = PayloadCatalog.ParsePhpReleases(PhpReleases);

        Assert.NotNull(p);
        Assert.Equal("php-8.4.26-nts-Win32-vs17-x64.zip", p!.FileName);
        Assert.Equal("https://windows.php.net/downloads/releases/php-8.4.26-nts-Win32-vs17-x64.zip", p.Url);
        Assert.Equal("8.4.26", p.Version);
        Assert.Equal("da68394f9193b7f6b89d0c76861a4034ae10efee7fd55a7255d8118c2acf70d7", p.Sha256);
        Assert.Null(p.ExpectedSize); // "33.48MB" is not a byte count; the checksum is what protects the download
    }

    [Fact]
    public void Php_resolution_returns_null_when_the_series_is_missing()
    {
        Assert.Null(PayloadCatalog.ParsePhpReleases("""{ "8.3": { "version": "8.3.30" } }"""));
    }

    [Fact]
    public void Nginx_resolution_reads_the_stable_link_not_the_mainline_one()
    {
        const string page = """
            <h4 id="mainline">Mainline version</h4>
            <a href="/download/nginx-1.31.6.zip">nginx/Windows-1.31.6</a>
            <h4 id="stable">Stable version</h4>
            <a href="/download/nginx-1.30.5.zip">nginx/Windows-1.30.5</a>
            <h4 id="legacy">Legacy versions</h4>
            <a href="/download/nginx-1.28.3.zip">nginx/Windows-1.28.3</a>
            """;
        Assert.Equal("1.30.5", PayloadCatalog.ParseNginxStable(page));
        Assert.Null(PayloadCatalog.ParseNginxStable("<html>nothing here</html>"));
    }

    [Fact]
    public void Nginx_payload_strips_the_versioned_top_level_folder()
    {
        var p = PayloadCatalog.NginxFor("1.30.5");
        Assert.True(p.StripTopLevel);
        Assert.Equal("nginx-1.30.5.zip", p.FileName);
    }

    [Fact]
    public void App_bundle_names_are_recognised()
    {
        Assert.True(GitHubReleases.IsAppBundle("inkspire-app-v1.2.3.zip"));
        Assert.True(GitHubReleases.IsAppBundle("Inkspire-App-v1.2.3.ZIP"));
        Assert.False(GitHubReleases.IsAppBundle("inkspire-app-v1.2.3.zip.sha256"));
        Assert.False(GitHubReleases.IsAppBundle("InkspirePanel.exe"));
    }

    [Fact]
    public void Release_parsing_pairs_the_bundle_with_its_checksum_file()
    {
        const string json = """
            {
              "tag_name": "v1.4.0",
              "published_at": "2026-10-01T09:00:00Z",
              "assets": [
                { "name": "inkspire-app-v1.4.0.zip.sha256", "browser_download_url": "https://x/inkspire-app-v1.4.0.zip.sha256", "size": 90 },
                { "name": "inkspire-app-v1.4.0.zip", "browser_download_url": "https://x/inkspire-app-v1.4.0.zip", "size": 41234567 },
                { "name": "notes.txt", "browser_download_url": "https://x/notes.txt", "size": 5 }
              ]
            }
            """;
        var rel = GitHubReleases.Parse(json, GitHubReleases.IsAppBundle);

        Assert.NotNull(rel);
        Assert.Equal("v1.4.0", rel!.Tag);
        Assert.Equal("inkspire-app-v1.4.0.zip", rel.AssetName);
        Assert.Equal(41234567, rel.Size);
        Assert.Equal("https://x/inkspire-app-v1.4.0.zip.sha256", rel.Sha256Url);
    }

    [Fact]
    public void A_release_without_a_bundle_is_not_an_update()
    {
        Assert.Null(GitHubReleases.Parse("""{ "tag_name": "v1", "assets": [ { "name": "x.exe", "browser_download_url": "u", "size": 1 } ] }""", GitHubReleases.IsAppBundle));
    }

    [Theory]
    [InlineData("da68394f9193b7f6b89d0c76861a4034ae10efee7fd55a7255d8118c2acf70d7  inkspire-app-v1.zip", true)]
    [InlineData("DA68394F9193B7F6B89D0C76861A4034AE10EFEE7FD55A7255D8118C2ACF70D7 *x.zip\n", true)]
    [InlineData("not-a-hash file.zip", false)]
    [InlineData("", false)]
    public void Checksum_sidecars_are_parsed_strictly(string text, bool valid)
    {
        Assert.Equal(valid, GitHubReleases.ParseSha256(text) is not null);
    }

    [Theory]
    [InlineData("v1.0.0", "v1.0.1", true)]
    [InlineData("1.2.0", "v1.10.0", true)]
    [InlineData("v1.2.0", "1.2.0", false)]
    [InlineData("v1.3.0", "v1.2.9", false)]
    [InlineData("dev", "v1.0.0", true)]
    public void Version_comparison(string installed, string latest, bool newer)
    {
        Assert.Equal(newer, GitHubReleases.IsNewer(installed, latest));
    }

    [Fact]
    public void Zip_common_root_is_only_stripped_when_every_entry_shares_it()
    {
        Assert.Equal("nginx-1.30.5", ZipExtractor.CommonRoot(new[] { "nginx-1.30.5/", "nginx-1.30.5/nginx.exe", "nginx-1.30.5/conf/nginx.conf" }));
        Assert.Null(ZipExtractor.CommonRoot(new[] { "php.exe", "ext/php_curl.dll" }));
        Assert.Null(ZipExtractor.CommonRoot(new[] { "inkspire/artisan", "other/file" }));
    }
}
