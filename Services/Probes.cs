using System.Net.Sockets;

namespace Inkspire.Panel.Services;

public enum HealthLevel
{
    Ok,
    Degraded,
    Down,
}

public sealed record HealthResult(HealthLevel Level, string? Message)
{
    public static readonly HealthResult Healthy = new(HealthLevel.Ok, null);
    public static HealthResult Degraded(string message) => new(HealthLevel.Degraded, message);
    public static HealthResult Down(string message) => new(HealthLevel.Down, message);
}

public interface IHealthProbe
{
    Task<HealthResult> ProbeAsync(CancellationToken ct);
}

/// <summary>Healthy when something accepts a TCP connection (php-cgi workers listen on loopback).</summary>
public sealed class TcpPortProbe : IHealthProbe
{
    private readonly string _host;
    private readonly int _port;

    public TcpPortProbe(int port, string host = "127.0.0.1")
    {
        _port = port;
        _host = host;
    }

    public async Task<HealthResult> ProbeAsync(CancellationToken ct)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(1500));
        try
        {
            await client.ConnectAsync(_host, _port, cts.Token).ConfigureAwait(false);
            return HealthResult.Healthy;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return HealthResult.Down($"port {_port} did not answer");
        }
        catch (SocketException ex)
        {
            return HealthResult.Down($"port {_port}: {ex.SocketErrorCode}");
        }
    }
}

/// <summary>GET a URL and classify the status code.</summary>
public sealed class HttpProbe : IHealthProbe
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly Func<int, HealthResult> _classify;

    public HttpProbe(HttpClient http, string url, Func<int, HealthResult>? classify = null)
    {
        _http = http;
        _url = url;
        _classify = classify ?? (status => status is >= 200 and < 300 ? HealthResult.Healthy : HealthResult.Degraded($"HTTP {status}"));
    }

    public async Task<HealthResult> ProbeAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var response = await _http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return _classify((int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return HealthResult.Down("no answer within 4 s");
        }
        catch (HttpRequestException ex)
        {
            return HealthResult.Down(ex.InnerException?.Message ?? ex.Message);
        }
    }
}
