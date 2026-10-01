using System.Text.Json;
using Inkspire.Panel.App;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.Setup;

public enum PayloadKind
{
    Zip,
    Exe,
    File,
}

/// <summary>One downloadable component.</summary>
public sealed record Payload(
    string Id,
    string Title,
    string Url,
    string FileName,
    PayloadKind Kind,
    string Version,
    bool StripTopLevel = false,
    string? Sha256 = null,
    long? ExpectedSize = null);

public sealed class NoAppReleaseException : Exception
{
    public NoAppReleaseException(string repo)
        : base($"{repo} has no published release with an inkspire-app-*.zip file yet. Choose a local app bundle (.zip) instead, or publish a release first.")
    {
    }
}

/// <summary>Download sources. PHP and the app bundle are resolved live (releases.json / GitHub latest); the rest are fixed
/// URLs. Anything already in <c>downloads\</c> is reused, which also makes an offline install possible.</summary>
public static class PayloadCatalog
{
    public const string PhpSeries = "8.4";
    /// <summary>Used only when windows.php.net's releases.json cannot be read.</summary>
    public const string PhpPinned = "8.4.26";
    /// <summary>Used only when nginx.org's download page cannot be read. nginx.org keeps just the newest patch of each
    /// branch, so a pinned version rots; the stable one is normally looked up live.</summary>
    public const string NginxPinned = "1.30.5";

    public static Payload NginxFor(string version) => new("nginx", $"nginx {version}",
        $"https://nginx.org/download/nginx-{version}.zip", $"nginx-{version}.zip", PayloadKind.Zip, version, StripTopLevel: true);

    /// <summary>The "Stable version" zip listed on nginx.org (falls back to <see cref="NginxPinned"/>). nginx.org publishes no
    /// checksums for the zips; the download is protected by HTTPS only.</summary>
    public static async Task<Payload> ResolveNginxAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            var html = await http.GetStringAsync("https://nginx.org/en/download.html", ct).ConfigureAwait(false);
            if (ParseNginxStable(html) is { } version) return NginxFor(version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"nginx.org lookup failed, using pinned {NginxPinned}: {ex.Message}");
        }
        return NginxFor(NginxPinned);
    }

    /// <summary>The first nginx-x.y.z.zip link after the "Stable version" heading of nginx.org/en/download.html.</summary>
    public static string? ParseNginxStable(string html)
    {
        var idx = html.IndexOf("Stable version", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var m = System.Text.RegularExpressions.Regex.Match(html[idx..], @"/download/nginx-(\d+\.\d+\.\d+)\.zip");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static Payload CaBundle => new("cacert", "CA certificate bundle",
        "https://curl.se/ca/cacert.pem", "cacert.pem", PayloadKind.File, "latest");

    public static Payload VcRedist => new("vcredist", "Visual C++ 2015-2022 runtime (x64)",
        "https://aka.ms/vs/17/release/vc_redist.x64.exe", "vc_redist.x64.exe", PayloadKind.Exe, "17");

    public static Payload PhpPinnedPayload => new("php", $"PHP {PhpPinned} (NTS x64)",
        $"https://windows.php.net/downloads/releases/archives/php-{PhpPinned}-nts-Win32-vs17-x64.zip",
        $"php-{PhpPinned}-nts-Win32-vs17-x64.zip", PayloadKind.Zip, PhpPinned);

    /// <summary>Latest PHP 8.4 NTS x64 from releases.json (falls back to the pinned archive URL).</summary>
    public static async Task<Payload> ResolvePhpAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync("https://windows.php.net/downloads/releases/releases.json", ct).ConfigureAwait(false);
            if (ParsePhpReleases(json) is { } payload) return payload;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"PHP releases.json lookup failed, using pinned {PhpPinned}: {ex.Message}");
        }
        return PhpPinnedPayload;
    }

    /// <summary>Picks the NTS x64 zip of <see cref="PhpSeries"/> out of releases.json.</summary>
    public static Payload? ParsePhpReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(PhpSeries, out var series)) return null;
        var version = series.GetProperty("version").GetString() ?? PhpPinned;
        foreach (var prop in series.EnumerateObject())
        {
            // keys look like "nts-vs17-x64"; skip debug packs, x86 and thread-safe builds
            if (!prop.Name.StartsWith("nts-", StringComparison.OrdinalIgnoreCase) || !prop.Name.EndsWith("-x64", StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.Value.ValueKind != JsonValueKind.Object || !prop.Value.TryGetProperty("zip", out var zip)) continue;
            var path = zip.GetProperty("path").GetString()!;
            var sha = zip.TryGetProperty("sha256", out var s) ? s.GetString() : null;
            long? size = zip.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : null;
            return new Payload("php", $"PHP {version} (NTS x64)", "https://windows.php.net/downloads/releases/" + path, path,
                PayloadKind.Zip, version, Sha256: sha, ExpectedSize: size);
        }
        return null;
    }

    public static async Task<Payload> ResolveAppAsync(HttpClient http, CancellationToken ct)
    {
        var rel = await GitHubReleases.LatestAppAsync(http, ct).ConfigureAwait(false)
                  ?? throw new NoAppReleaseException(GitHubReleases.AppRepo);
        var sha = await GitHubReleases.ReadSha256Async(http, rel.Sha256Url, ct).ConfigureAwait(false);
        return new Payload("app", $"Inkspire {rel.Tag}", rel.AssetUrl, rel.AssetName, PayloadKind.Zip, rel.Tag,
            StripTopLevel: true, Sha256: sha, ExpectedSize: rel.Size);
    }

    /// <summary>A bundle the user picked from disk (before the first release exists, or offline).</summary>
    public static Payload LocalApp(string zipPath, string version) =>
        new("app", $"Inkspire (local bundle {Path.GetFileName(zipPath)})", new Uri(Path.GetFullPath(zipPath)).AbsoluteUri,
            Path.GetFileName(zipPath), PayloadKind.Zip, version, StripTopLevel: true);
}
