using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Inkspire.Panel.App;

namespace Inkspire.Panel.Setup;

public sealed record DownloadProgress(string Id, long Received, long? Total, double BytesPerSecond)
{
    public double? Fraction => Total is > 0 ? (double)Received / Total.Value : null;
}

/// <summary>Resumable downloads into the <c>downloads\</c> cache with size and SHA-256 verification. A file already in the
/// cache (put there for an offline install) is reused; a local <c>file://</c> payload is copied instead of fetched.</summary>
public sealed class DownloadManager
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;

    public DownloadManager(HttpClient http, string cacheDir)
    {
        _http = http;
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
    }

    public string CachePath(Payload p) => Path.Combine(_cacheDir, p.FileName);

    public bool IsCached(Payload p)
    {
        var path = CachePath(p);
        if (!File.Exists(path)) return false;
        var len = new FileInfo(path).Length;
        if (len == 0) return false;
        return p.ExpectedSize is not { } size || len == size;
    }

    /// <summary>Downloads (resuming a partial file) unless cached. Returns the local path.</summary>
    public async Task<string> FetchAsync(Payload p, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var final = CachePath(p);

        if (Uri.TryCreate(p.Url, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            // local bundle: use it where it is when it already lives in the cache folder, otherwise copy it in
            var source = uri.LocalPath;
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(final), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, final, overwrite: true);
            progress?.Report(new DownloadProgress(p.Id, new FileInfo(final).Length, new FileInfo(final).Length, 0));
            return final;
        }

        if (IsCached(p))
        {
            if (p.Sha256 is not null && !await VerifyAsync(final, p.Sha256, ct).ConfigureAwait(false))
            {
                Log.Warn($"{p.FileName}: cached file failed its SHA-256 check, downloading again");
                File.Delete(final);
            }
            else
            {
                var len = new FileInfo(final).Length;
                progress?.Report(new DownloadProgress(p.Id, len, len, 0));
                return final;
            }
        }

        var part = final + ".part";
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, p.Url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // We already hold everything, or the file changed on the server. Start over to be safe.
            File.Delete(part);
            return await FetchAsync(p, progress, ct).ConfigureAwait(false);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{p.Title}: HTTP {(int)response.StatusCode} from {p.Url}");

        var resumed = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
        if (!resumed) existing = 0;
        long? total = response.Content.Headers.ContentLength is { } cl ? cl + existing : p.ExpectedSize;

        await using (var fs = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        await using (var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var buffer = new byte[1 << 16];
            var received = existing;
            var clock = Stopwatch.StartNew();
            long lastReportMs = 0;
            var windowStart = received;
            int read;
            while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                if (clock.ElapsedMilliseconds - lastReportMs < 250) continue;
                var seconds = (clock.ElapsedMilliseconds - lastReportMs) / 1000.0;
                progress?.Report(new DownloadProgress(p.Id, received, total, seconds > 0 ? (received - windowStart) / seconds : 0));
                lastReportMs = clock.ElapsedMilliseconds;
                windowStart = received;
            }
            progress?.Report(new DownloadProgress(p.Id, received, total ?? received, 0));
        }

        var size = new FileInfo(part).Length;
        if (p.ExpectedSize is { } expect && size != expect)
        {
            File.Delete(part);
            throw new IOException($"{p.Title}: downloaded {size} bytes but expected {expect}.");
        }
        if (p.Sha256 is not null && !await VerifyAsync(part, p.Sha256, ct).ConfigureAwait(false))
        {
            File.Delete(part);
            throw new IOException($"{p.Title}: SHA-256 mismatch. The download is corrupt or has been tampered with.");
        }

        if (File.Exists(final)) File.Delete(final);
        File.Move(part, final);
        return final;
    }

    public static async Task<bool> VerifyAsync(string path, string expectedHex, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
