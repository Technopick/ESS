namespace Ess.Integration.Nav.Transport;

/// <summary>Exponential backoff with full jitter, capped. Pure function so it is unit-testable.</summary>
public static class RetrySchedule
{
    public static TimeSpan NextDelay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, Random? rng = null)
    {
        if (attempt < 1) attempt = 1;
        var exp = Math.Min(baseDelay.TotalSeconds * Math.Pow(2, attempt - 1), maxDelay.TotalSeconds);
        var jitter = (rng ?? Random.Shared).NextDouble();
        // Keep at least half the exponential delay so retries never stampede.
        return TimeSpan.FromSeconds(exp / 2 + exp / 2 * jitter);
    }
}
