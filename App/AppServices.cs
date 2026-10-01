using System.Net.Http.Headers;
using Inkspire.Panel.Config;
using Inkspire.Panel.Processes;
using Inkspire.Panel.Services;

namespace Inkspire.Panel.App;

/// <summary>Composition root for panel mode. Built once in <c>Program</c> after the install root is known.</summary>
public sealed class AppServices : IDisposable
{
    public const string UserAgent = "InkspirePanel/1.0 (+https://github.com/jhemmmm/inkspire-installer)";

    private static AppServices? _current;

    private AppServices(AppPaths paths, ConfigStore store, PanelConfig config)
    {
        Paths = paths;
        Store = store;
        Config = config;
        Http = CreateHttpClient();
        Job = new JobObject();
        Services = new ServiceManager(this);
    }

    public static AppServices Current => _current ?? throw new InvalidOperationException("AppServices not initialized");
    public static bool IsInitialized => _current is not null;

    public AppPaths Paths { get; }
    public ConfigStore Store { get; }
    public PanelConfig Config { get; private set; }
    public HttpClient Http { get; }
    /// <summary>KILL_ON_JOB_CLOSE job every child process joins.</summary>
    public JobObject Job { get; }
    public ServiceManager Services { get; }

    public static AppServices Initialize(AppPaths paths, ConfigStore store, PanelConfig config)
    {
        Log.Configure(paths.LogsDir);
        _current = new AppServices(paths, store, config);
        return _current;
    }

    public void SaveConfig()
    {
        try
        {
            Store.Save(Config);
        }
        catch (Exception ex)
        {
            Log.Error("Saving panel.json failed", ex);
        }
    }

    public void ReloadConfig() => Config = Store.Load();

    /// <summary>Swap in a new configuration object. Services read <see cref="Config"/> on every use, never cache it.</summary>
    public void ApplyConfig(PanelConfig config) => Config = config;

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return http;
    }

    public void Dispose()
    {
        Services.Dispose();
        Job.Dispose();
        Http.Dispose();
    }
}
