namespace Ess.Application.Integration;

/// <summary>How a NAV failure should be handled by the retry engine.</summary>
public enum NavErrorCategory
{
    None = 0,
    /// <summary>Timeout, 5xx, 429, connection refused: retry with backoff.</summary>
    Transient = 1,
    /// <summary>NAV rejected the business data (e.g. insufficient balance): do not retry, return to the user.</summary>
    Validation = 2,
    /// <summary>401/403: credentials or permissions; stop and alert an integration admin.</summary>
    Authentication = 3,
    NotFound = 4,
    /// <summary>Missing endpoint mapping, unpublished service, wrong company: admin must fix configuration.</summary>
    Configuration = 5,
    /// <summary>The request may or may not have reached NAV (e.g. timeout after send). Reconcile before retrying.</summary>
    Unknown = 6
}

public sealed record NavError(NavErrorCategory Category, string Message, string? NavErrorCode = null, int? HttpStatus = null);

public sealed class NavResult<T>
{
    public bool Success { get; private init; }
    public T? Value { get; private init; }
    public NavError? Error { get; private init; }

    public static NavResult<T> Ok(T value) => new() { Success = true, Value = value };
    public static NavResult<T> Fail(NavError error) => new() { Success = false, Error = error };
    public static NavResult<T> Fail(NavErrorCategory category, string message, int? httpStatus = null) =>
        Fail(new NavError(category, message, null, httpStatus));

    public NavResult<TOut> Map<TOut>(Func<T, TOut> map) =>
        Success ? NavResult<TOut>.Ok(map(Value!)) : NavResult<TOut>.Fail(Error!);
}

/// <summary>NAV's confirmation that it accepted a transaction (rule 11: only this counts as posted).</summary>
public sealed record NavSubmissionReceipt(string NavDocumentNo, string? NavStatus, DateTime ConfirmedAtUtc);

/// <summary>A page of records from a sync read, with the watermark to resume from.</summary>
public sealed record NavPage<T>(IReadOnlyList<T> Items, string? NextWatermark, bool HasMore, string? ContinuationToken = null);
