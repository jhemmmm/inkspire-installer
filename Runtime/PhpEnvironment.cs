using Inkspire.Panel.App;

namespace Inkspire.Panel.Runtime;

/// <summary>Environment every PHP process the panel starts shares (php-cgi workers, scheduler, artisan runs).</summary>
public static class PhpEnvironment
{
    public static IReadOnlyDictionary<string, string> Build(AppPaths paths, int maxRequests) => new Dictionary<string, string>
    {
        // php-cgi (Windows) has no fork: it serves N requests, exits cleanly, and the supervisor respawns it.
        ["PHP_FCGI_MAX_REQUESTS"] = maxRequests.ToString(),
        // Laravel's scheduler and Process facade spawn "php artisan ..." through this.
        ["PHP_BINARY"] = paths.PhpExe,
        ["PHPRC"] = paths.PhpDir,
        // Prepended to PATH by ManagedProcess so the ICU/OpenSSL DLLs beside php.exe always win.
        ["PATH"] = paths.PhpDir,
    };
}
