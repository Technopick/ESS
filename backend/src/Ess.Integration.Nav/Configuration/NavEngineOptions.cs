namespace Ess.Integration.Nav.Configuration;

/// <summary>Engine-wide options (appsettings "NavIntegration"). Per-instance connection settings live in the database.</summary>
public sealed class NavEngineOptions
{
    public const string Section = "NavIntegration";

    public int QueueBatchSize { get; set; } = 20;
    public int QueuePollSeconds { get; set; } = 10;
    public int QueueLockSeconds { get; set; } = 120;
    public int RetryBaseDelaySeconds { get; set; } = 15;
    public int RetryMaxDelayMinutes { get; set; } = 60;
    public int LogBodyMaxChars { get; set; } = 8000;
    public int LogRetentionDays { get; set; } = 180;
    /// <summary>Extra field names to mask in logs, on top of the built-in list.</summary>
    public string[] AdditionalSensitiveFields { get; set; } = Array.Empty<string>();
    /// <summary>The simulator may only be used when this is true AND the host environment is not Production.</summary>
    public bool AllowSimulator { get; set; }
}
