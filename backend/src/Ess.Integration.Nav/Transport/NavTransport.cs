using System.Diagnostics;
using Ess.Application.Integration;
using Ess.Domain.Integration;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Transport;

public sealed record NavCall(NavProtocol Protocol, string Operation, HttpMethod Method, string Url, string? Body,
    string? ContentType = null, IDictionary<string, string>? Headers = null, bool IsWrite = false);

public sealed record NavHttpResult(bool Success, int? StatusCode, string? Body, long DurationMs, NavError? Error);

/// <summary>Ambient correlation for a call: which queue item / sync job it belongs to.</summary>
public sealed class NavCallScope
{
    private static readonly AsyncLocal<(Guid? QueueItemId, Guid? SyncJobId, string? CorrelationId)> Current = new();
    public static (Guid? QueueItemId, Guid? SyncJobId, string? CorrelationId) Value => Current.Value;

    public static IDisposable Begin(Guid? queueItemId = null, Guid? syncJobId = null, string? correlationId = null)
    {
        var prev = Current.Value;
        Current.Value = (queueItemId, syncJobId, correlationId ?? Activity.Current?.TraceId.ToString());
        return new Restore(prev);
    }

    private sealed class Restore : IDisposable
    {
        private readonly (Guid?, Guid?, string?) _prev;
        public Restore((Guid?, Guid?, string?) prev) => _prev = prev;
        public void Dispose() => Current.Value = _prev;
    }
}

/// <summary>
/// Single choke point for every HTTP call to NAV: authentication, throttling, timing, error classification
/// and masked request/response logging into nav_sync_logs. Logs are written in their own DB scope so they
/// survive even when the calling business transaction rolls back.
/// </summary>
public sealed class NavTransport
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Throttles = new();
    private readonly NavAuthenticationManager _auth;
    private readonly SensitiveDataMasker _masker;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NavTransport> _log;

    public NavTransport(NavAuthenticationManager auth, SensitiveDataMasker masker, IServiceScopeFactory scopes, ILogger<NavTransport> log)
    {
        _auth = auth;
        _masker = masker;
        _scopes = scopes;
        _log = log;
    }

    public async Task<NavHttpResult> SendAsync(NavConnectionContext ctx, NavCall call, CancellationToken ct)
    {
        var throttle = Throttles.GetOrAdd(ctx.SettingsId, _ => new SemaphoreSlim(Math.Max(1, ctx.Settings.MaxConcurrency)));
        await throttle.WaitAsync(ct);
        var sw = Stopwatch.StartNew();
        var sent = false;
        NavHttpResult result;
        try
        {
            result = await SendOnceAsync(ctx, call, () => sent = true, ct);
            if (result.StatusCode == 401 && ctx.Settings.AuthMethod == NavAuthMethod.OAuthClientCredentials)
            {
                _auth.InvalidateToken(ctx.SettingsId); // token may have been revoked: refresh once
                sent = false;
                result = await SendOnceAsync(ctx, call, () => sent = true, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var category = NavErrorClassifier.FromException(ex, call.IsWrite, sent);
            result = new NavHttpResult(false, null, null, sw.ElapsedMilliseconds, new NavError(category, Describe(ex)));
        }
        finally
        {
            throttle.Release();
        }

        result = result with { DurationMs = sw.ElapsedMilliseconds };
        await LogAsync(ctx, call, result);
        return result;
    }

    private async Task<NavHttpResult> SendOnceAsync(NavConnectionContext ctx, NavCall call, Action markSent, CancellationToken ct)
    {
        var http = _auth.GetClient(ctx);
        using var req = new HttpRequestMessage(call.Method, call.Url);
        if (call.Body is not null)
            req.Content = new StringContent(call.Body, System.Text.Encoding.UTF8, call.ContentType ?? "application/json");
        if (call.Headers is not null)
            foreach (var (k, v) in call.Headers) req.Headers.TryAddWithoutValidation(k, v);
        req.Headers.TryAddWithoutValidation("Accept", call.Protocol == NavProtocol.Soap ? "text/xml" : "application/json");
        await _auth.AuthorizeAsync(req, ctx, ct);

        markSent();
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode) return new NavHttpResult(true, (int)resp.StatusCode, body, 0, null);

        var category = NavErrorClassifier.FromStatus(resp.StatusCode, call.IsWrite);
        var message = ExtractErrorMessage(body) ?? $"NAV returned HTTP {(int)resp.StatusCode}.";
        if (call.Protocol == NavProtocol.Soap && body.Contains("faultstring", StringComparison.OrdinalIgnoreCase))
            category = NavErrorClassifier.FromSoapFault(message);
        return new NavHttpResult(false, (int)resp.StatusCode, body, 0, new NavError(category, message, null, (int)resp.StatusCode));
    }

    private static string? ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(body, "<faultstring[^>]*>(?<m>[^<]*)</faultstring>");
        if (m.Success) return System.Net.WebUtility.HtmlDecode(m.Groups["m"].Value);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return msg.GetString();
        }
        catch (System.Text.Json.JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "NAV did not respond within the configured timeout.",
        HttpRequestException h => $"Could not reach NAV: {h.Message}",
        _ => ex.Message
    };

    private async Task LogAsync(NavConnectionContext ctx, NavCall call, NavHttpResult r)
    {
        try
        {
            var (queueId, jobId, corr) = NavCallScope.Value;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            db.NavSyncLogs.Add(new NavSyncLog
            {
                CompanyId = ctx.CompanyId == Guid.Empty ? null : ctx.CompanyId,
                SyncJobId = jobId,
                QueueItemId = queueId,
                Protocol = call.Protocol.ToString(),
                Operation = call.Operation,
                HttpMethod = call.Method.Method,
                Endpoint = _masker.MaskUrl(call.Url),
                StatusCode = r.StatusCode,
                Success = r.Success,
                DurationMs = r.DurationMs,
                RequestMasked = _masker.MaskBody(call.Body),
                ResponseMasked = _masker.MaskBody(r.Body),
                Error = r.Error?.Message,
                CorrelationId = corr,
                OccurredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Logging must never break the business call, but it must be visible.
            _log.LogError(ex, "Failed to write NAV call log for {Operation}", call.Operation);
        }
    }
}
