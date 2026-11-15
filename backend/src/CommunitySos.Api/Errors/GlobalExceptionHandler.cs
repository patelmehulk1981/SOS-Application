using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Api.Errors;

/// <summary>
/// Single place where exceptions become RFC 7807 responses. Every body carries a stable "code"
/// (for the app to localise), "traceId" and "correlationId". Internal details never leak:
/// unexpected errors return a generic message and are logged with the full exception.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    // Raised by database/scripts/020_triggers.sql
    private const int SqlImmutableReferral = 51001;
    private const int SqlAppendOnly = 51002;
    private const int SqlUniqueIndex = 2601;
    private const int SqlUniqueConstraint = 2627;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true; // client went away; nothing to report
        }

        var error = Map(exception);

        if (error.Status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {Status} {Code}: {Message}", error.Status, error.Code, exception.Message);
        }

        var problem = new ProblemDetails
        {
            Status = error.Status,
            Title = error.Title,
            Detail = error.Detail,
            Type = $"https://httpstatuses.io/{error.Status}",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = error.Code;
        if (error.Errors is not null)
        {
            problem.Extensions["errors"] = error.Errors;
        }

        if (exception is AppException { Details: { } details })
        {
            foreach (var (key, value) in details)
            {
                problem.Extensions[key] = value;
            }
        }

        httpContext.Response.StatusCode = error.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static MappedError Map(Exception exception) => exception switch
    {
        ValidationException v => new(400, "Validation failed", "validation_failed", "One or more fields are invalid.", ValidationErrors.From(v.Errors)),
        DomainException d => new(422, "Request cannot be processed", d.Code, d.Message),
        NotFoundException n => new(404, "Not found", n.Code, n.Message),
        ForbiddenException f => new(403, "Forbidden", f.Code, f.Message),
        AuthenticationFailedException a => new(401, "Authentication failed", a.Code, a.Message),
        ConflictException c => new(409, "Conflict", c.Code, c.Message),
        UnprocessableException u => new(422, "Request cannot be processed", u.Code, u.Message),
        DbUpdateConcurrencyException => new(409, "Conflict", "concurrency_conflict", "This record was changed by someone else. Reload and try again."),
        _ when FindSqlError(exception) is { } number => number switch
        {
            SqlUniqueIndex or SqlUniqueConstraint => new(409, "Conflict", "duplicate", "This record already exists."),
            SqlImmutableReferral or SqlAppendOnly => new(409, "Conflict", "immutable_record", "This record cannot be changed."),
            _ => Unexpected,
        },
        BadHttpRequestException b => new(b.StatusCode, "Bad request", "bad_request", "The request could not be read."),
        _ => Unexpected,
    };

    private static readonly MappedError Unexpected =
        new(500, "Server error", "unexpected_error", "Something went wrong on our side. Please try again.");

    private static int? FindSqlError(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql)
            {
                return sql.Number;
            }
        }

        return null;
    }

    private sealed record MappedError(int Status, string Title, string Code, string Detail, IDictionary<string, string[]>? Errors = null);
}
