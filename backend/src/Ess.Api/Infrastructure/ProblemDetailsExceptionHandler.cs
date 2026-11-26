using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ess.Api.Infrastructure;

/// <summary>Standardised error responses (RFC 7807). Internal details are logged, never returned to the client.</summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ProblemDetailsExceptionHandler> _log;
    private readonly IProblemDetailsService _problems;

    public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> log, IProblemDetailsService problems)
    {
        _log = log;
        _problems = problems;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            Ess.Api.Services.NavUnavailableException => (StatusCodes.Status503ServiceUnavailable, "NAV is not available"),
            Ess.Application.Workflow.WorkflowException => (StatusCodes.Status409Conflict, "This action is not possible right now"),
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record was changed by someone else. Reload and try again."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };
        if (status >= 500 && status != 503) _log.LogError(ex, "Unhandled exception for {Path}", ctx.Request.Path);

        var pd = new ProblemDetails { Status = status, Title = title, Instance = ctx.Request.Path };
        pd.Extensions["traceId"] = ctx.TraceIdentifier;
        if (ex is ValidationException ve)
            pd.Extensions["errors"] = ve.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        else if (status < 500 || status == 503)
            pd.Detail = ex.Message;

        ctx.Response.StatusCode = status;
        return await _problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd, Exception = ex });
    }
}
