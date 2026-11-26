using Ess.Domain.Common;

namespace Ess.Domain.Integration;

public enum NavPlatform
{
    /// <summary>NAV 2013 / 2013 R2.</summary>
    Nav2013 = 0,
    Nav2015 = 1,
    Nav2016 = 2,
    Nav2017 = 3,
    Nav2018 = 4,
    /// <summary>Business Central on-premises (v14 C/AL or v15+ AL).</summary>
    BusinessCentralOnPrem = 5,
    BusinessCentralOnline = 6
}

public enum NavAuthMethod
{
    /// <summary>Windows/NTLM or Negotiate (NAV default).</summary>
    Windows = 0,
    /// <summary>NavUserPassword over HTTP Basic.</summary>
    NavUserPassword = 1,
    /// <summary>BC web service access key over HTTP Basic (on-prem only from BC 2022 onwards).</summary>
    WebServiceAccessKey = 2,
    /// <summary>OAuth 2.0 client credentials via Microsoft Entra ID (required for BC online).</summary>
    OAuthClientCredentials = 3
}

/// <summary>Connection settings for one NAV/BC service instance. Secrets are stored encrypted only.</summary>
public class NavIntegrationSettings : AuditableEntity
{
    public required string Name { get; set; }
    public NavPlatform Platform { get; set; }
    public string? ServerHost { get; set; }
    public string? ServiceInstance { get; set; }
    public string? SoapBaseUrl { get; set; }
    public string? ODataBaseUrl { get; set; }
    public string? ApiBaseUrl { get; set; }
    /// <summary>BC online environment name (e.g. "Production", "Sandbox").</summary>
    public string? EnvironmentName { get; set; }
    public string? TenantId { get; set; }
    public NavAuthMethod AuthMethod { get; set; }
    public string? UserName { get; set; }
    public string? Domain { get; set; }
    public string? PasswordEncrypted { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecretEncrypted { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public int SyncIntervalMinutes { get; set; } = 15;
    public bool ValidateServerCertificate { get; set; } = true;
    /// <summary>Max concurrent calls to this instance (integration throttling).</summary>
    public int MaxConcurrency { get; set; } = 4;
    public bool IsEnabled { get; set; } = true;
    /// <summary>When true the engine uses the built-in simulator (dev/test only; refused in Production).</summary>
    public bool UseSimulator { get; set; }

    public DateTime? LastHealthCheckAtUtc { get; set; }
    public bool? LastHealthCheckOk { get; set; }
    public string? LastHealthCheckMessage { get; set; }
}

/// <summary>
/// Maps a logical ESS operation/entity to the NAV object that serves it on this instance.
/// This is how customer-specific NAV objects are plugged in without code changes (spec §11 "Map ESS fields to NAV fields").
/// </summary>
public class NavEndpointMapping : AuditableEntity
{
    public Guid IntegrationSettingsId { get; set; }
    /// <summary>Logical name, e.g. "Employee", "LeaveBalance", "Leave.Submit", "Payslip.Lines".</summary>
    public required string LogicalName { get; set; }
    /// <summary>SOAP, OData or BcApi.</summary>
    public required string Protocol { get; set; }
    /// <summary>Published service name (SOAP page/codeunit or OData entity set) or BC API route (publisher/group/version/entity).</summary>
    public required string ServiceName { get; set; }
    /// <summary>Codeunit method for SOAP codeunit calls.</summary>
    public string? Method { get; set; }
    /// <summary>JSON object mapping ESS field names to NAV field names.</summary>
    public string? FieldMapJson { get; set; }
    /// <summary>Field used for incremental sync (e.g. "Last_Date_Modified", "SystemModifiedAt").</summary>
    public string? ModifiedField { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public enum QueueItemStatus { Pending = 0, Processing = 1, Succeeded = 2, Failed = 3, DeadLetter = 4, RequiresReconciliation = 5, Cancelled = 6 }

/// <summary>
/// Outbox of ESS → NAV transactions. Rows are written in the same DB transaction as the business change,
/// so nothing is lost if NAV is down, and failed rows are never deleted (spec §11).
/// </summary>
public class IntegrationQueueItem : Entity
{
    public Guid CompanyId { get; set; }
    public required string Operation { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    /// <summary>Stable key sent to NAV so a retried call cannot create a duplicate document.</summary>
    public required string IdempotencyKey { get; set; }
    /// <summary>Request payload. Encrypted when the operation carries personal or payroll data.</summary>
    public required string PayloadJson { get; set; }
    public bool PayloadEncrypted { get; set; }
    public QueueItemStatus Status { get; set; }
    public int Priority { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 8;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? NavDocumentNo { get; set; }
    public string? LastError { get; set; }
    public string? LastErrorCategory { get; set; }
    public string? CorrelationId { get; set; }
    public string? CreatedBy { get; set; }
}

public enum SyncJobStatus { Queued = 0, Running = 1, Succeeded = 2, PartiallySucceeded = 3, Failed = 4 }

/// <summary>One run of a NAV → ESS synchronization (scheduled or on demand).</summary>
public class NavSyncJob : Entity
{
    public Guid CompanyId { get; set; }
    public required string EntityName { get; set; }
    public bool IsFullSync { get; set; }
    public required string TriggeredBy { get; set; }
    public SyncJobStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsDeactivated { get; set; }
    public int RecordsFailed { get; set; }
    /// <summary>High-water mark used as the starting point of the next incremental run.</summary>
    public string? Watermark { get; set; }
    public string? Error { get; set; }
}

/// <summary>Log of every call to NAV, with secrets and sensitive fields masked (spec §3.4).</summary>
public class NavSyncLog : Entity
{
    public Guid? CompanyId { get; set; }
    public Guid? SyncJobId { get; set; }
    public Guid? QueueItemId { get; set; }
    public required string Protocol { get; set; }
    public required string Operation { get; set; }
    public required string HttpMethod { get; set; }
    public required string Endpoint { get; set; }
    public int? StatusCode { get; set; }
    public bool Success { get; set; }
    public long DurationMs { get; set; }
    public string? RequestMasked { get; set; }
    public string? ResponseMasked { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
