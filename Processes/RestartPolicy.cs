namespace Inkspire.Panel.Processes;

/// <summary>When and how a supervised process is restarted after it exits.</summary>
public sealed class RestartPolicy
{
    /// <summary>Wait before attempt N (the last entry repeats).</summary>
    public IReadOnlyList<TimeSpan> Delays { get; init; } = new[]
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
    };

    /// <summary>Give up (state Failed) after this many consecutive failures. 0 = keep trying forever.</summary>
    public int MaxConsecutiveFailures { get; init; } = 5;

    /// <summary>A run at least this long resets the failure counter.</summary>
    public TimeSpan StableAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>True for an exit that is routine (php-cgi recycled after PHP_FCGI_MAX_REQUESTS): respawn at once, no failure counted.</summary>
    public Func<ProcessExit, bool>? IsExpectedExit { get; init; }

    /// <summary>Human explanation for the log viewer / status line, or null for the generic message.</summary>
    public Func<ProcessExit, string?>? Describe { get; init; }

    public TimeSpan DelayFor(int consecutiveFailures)
    {
        var index = Math.Clamp(consecutiveFailures - 1, 0, Delays.Count - 1);
        return Delays[index];
    }
}
