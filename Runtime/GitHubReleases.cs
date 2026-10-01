using System.Text.Json;

namespace Inkspire.Panel.Runtime;

/// <summary>One asset of a GitHub release. <see cref="Sha256Url"/> is the optional "&lt;asset&gt;.sha256" sidecar.</summary>
public sealed record GitHubRelease(string Tag, string AssetName, string AssetUrl, long Size, string? Sha256Url, DateTime PublishedAt);

/// <summary><c>releases/latest</c> lookups (public repositories, unauthenticated: 60 requests per hour per address).</summary>
public static class GitHubReleases
{
    public const string AppRepo = "jhemmmm/Inkspire";

    public static bool IsAppBundle(string assetName) =>
        assetName.StartsWith("inkspire-app-", StringComparison.OrdinalIgnoreCase) && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static Task<GitHubRelease?> LatestAppAsync(HttpClient http, CancellationToken ct) => LatestAsync(http, AppRepo, IsAppBundle, ct);

    /// <summary>Null when the repo has no release yet or none carries a matching asset.</summary>
    public static async Task<GitHubRelease?> LatestAsync(HttpClient http, string repo, Func<string, bool> assetMatch, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false), assetMatch);
    }

    public static GitHubRelease? Parse(string json, Func<string, bool> assetMatch)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTime() : DateTime.MinValue;

        string? name = null, url = null;
        long size = 0;
        var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var n = asset.GetProperty("name").GetString() ?? "";
            var u = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (n.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) checksums[n] = u;
            else if (name is null && assetMatch(n))
            {
                name = n;
                url = u;
                size = asset.GetProperty("size").GetInt64();
            }
        }
        if (name is null || url is null) return null;
        checksums.TryGetValue(name + ".sha256", out var sha);
        return new GitHubRelease(tag, name, url, size, sha, published);
    }

    /// <summary>Reads the hex digest from a "&lt;hash&gt;  &lt;file&gt;" sidecar; null when unavailable or malformed.</summary>
    public static async Task<string?> ReadSha256Async(HttpClient http, string? sha256Url, CancellationToken ct)
    {
        if (sha256Url is null) return null;
        try
        {
            return ParseSha256(await http.GetStringAsync(sha256Url, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public static string? ParseSha256(string text)
    {
        var hex = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return hex is { Length: 64 } && hex.All(Uri.IsHexDigit) ? hex.ToLowerInvariant() : null;
    }

    /// <summary>"v1.2.3" newer than "1.2.0"? Unparsable versions count as newer when they differ (e.g. a "dev" bundle).</summary>
    public static bool IsNewer(string installed, string latest)
    {
        static string Norm(string v) => v.Trim().TrimStart('v', 'V');
        var a = Norm(installed);
        var b = Norm(latest);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return vb > va;
        return true;
    }
}
