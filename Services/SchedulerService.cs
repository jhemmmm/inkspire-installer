using Inkspire.Panel.App;
using Inkspire.Panel.Processes;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.Services;

/// <summary><c>php artisan schedule:work</c>: runs Inkspire's Laravel scheduler every minute (today: the daily
/// accounts-receivable reminder mail).</summary>
public sealed class SchedulerService : SupervisedService
{
    public SchedulerService(AppServices ctx) : base("scheduler", "Scheduler (artisan)", ctx)
    {
    }

    public override string Description => "Runs Inkspire's scheduled tasks (receivable reminders) every minute.";

    public override IReadOnlyList<LogSource> LogSources => new[]
    {
        new LogSource("scheduler (console)", Sink, null, null),
        new LogSource("Inkspire (laravel.log)", null, Ctx.Paths.LaravelLogsDir, "laravel*.log"),
    };

    protected override RestartPolicy Policy => new()
    {
        Delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60) },
        MaxConsecutiveFailures = 0, // a scheduler that cannot start yet (e.g. mid-update) keeps trying
        Describe = exit => exit.Uptime < TimeSpan.FromSeconds(3) ? "artisan exited immediately (check laravel.log and .env)" : null,
    };

    protected override ManagedProcessOptions BuildOptions() => new()
    {
        FileName = Ctx.Paths.PhpExe,
        Arguments = new[] { Ctx.Paths.AppArtisan, "schedule:work", "-n", "--no-ansi" },
        WorkingDirectory = Ctx.Paths.PhpDir,
        Environment = PhpEnvironment.Build(Ctx.Paths, Ctx.Config.Web.MaxRequests),
        GracefulStop = GracefulStopMode.None,
    };

    protected override Task PreflightAsync(CancellationToken ct)
    {
        RequireFile(Ctx.Paths.PhpExe, "php.exe");
        RequireFile(Ctx.Paths.AppArtisan, "The Inkspire app (artisan)");
        RequireFile(Ctx.Paths.AppEnvFile, "The app's .env file");
        return Task.CompletedTask;
    }
}
