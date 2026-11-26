using System.Net;
using Ess.Application.Integration;

namespace Ess.Integration.Nav.Transport;

/// <summary>Retry and Error Handling Engine, part 1: decides whether a failure is retryable (spec §3, component 9).</summary>
public static class NavErrorClassifier
{
    public static NavErrorCategory FromStatus(HttpStatusCode status, bool requestWasWrite) => (int)status switch
    {
        401 or 403 => NavErrorCategory.Authentication,
        404 => NavErrorCategory.NotFound,
        408 or 429 or 502 or 503 or 504 => requestWasWrite && (int)status is 502 or 504
            ? NavErrorCategory.Unknown      // gateway may have forwarded the write: reconcile before retry
            : NavErrorCategory.Transient,
        400 or 409 or 412 or 422 => NavErrorCategory.Validation,
        // A bare 500 on a write is ambiguous; the SOAP client re-classifies 500s that carry a NAV fault
        // (C/AL or AL ERROR() calls) through FromSoapFault, which is usually business validation.
        500 => requestWasWrite ? NavErrorCategory.Unknown : NavErrorCategory.Transient,
        _ when (int)status >= 500 => NavErrorCategory.Transient,
        _ => NavErrorCategory.Validation
    };

    public static NavErrorCategory FromException(Exception ex, bool requestWasWrite, bool requestSent) => ex switch
    {
        TaskCanceledException or TimeoutException when requestWasWrite && requestSent => NavErrorCategory.Unknown,
        TaskCanceledException or TimeoutException => NavErrorCategory.Transient,
        HttpRequestException { StatusCode: { } s } => FromStatus(s, requestWasWrite),
        HttpRequestException => NavErrorCategory.Transient, // DNS, refused, TLS reset: nothing reached NAV
        Configuration.NavConfigurationException => NavErrorCategory.Configuration,
        _ => NavErrorCategory.Transient
    };

    /// <summary>
    /// SOAP faults from NAV carry the C/AL/AL error text. Distinguish infrastructure faults
    /// (locking, deadlocks, service tier restarts) from business validation errors.
    /// </summary>
    public static NavErrorCategory FromSoapFault(string faultString)
    {
        var f = faultString.ToLowerInvariant();
        if (f.Contains("deadlock") || f.Contains("locked by another user") || f.Contains("timeout")
            || f.Contains("server is busy") || f.Contains("service is unavailable"))
            return NavErrorCategory.Transient;
        if (f.Contains("not have the following permissions") || f.Contains("permission"))
            return NavErrorCategory.Authentication;
        if (f.Contains("does not exist") && f.Contains("codeunit"))
            return NavErrorCategory.Configuration;
        return NavErrorCategory.Validation;
    }

    public static bool IsRetryable(NavErrorCategory c) => c is NavErrorCategory.Transient;
}
