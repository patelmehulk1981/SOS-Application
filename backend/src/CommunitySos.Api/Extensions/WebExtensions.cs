using System.Diagnostics;
using CommunitySos.Api.Middleware;
using CommunitySos.Api.Security;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

namespace CommunitySos.Api.Extensions;

public static class WebExtensions
{
    private const string OpenApiDocumentPath = "/openapi/v1.json";

    // ---------- ProblemDetails ----------

    /// <summary>Adds traceId and correlationId to every ProblemDetails the app produces.</summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
            if (CorrelationIdMiddleware.Get(ctx.HttpContext) is { } correlationId)
            {
                ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
            }

            if (!ctx.ProblemDetails.Extensions.ContainsKey("code") && ctx.ProblemDetails.Status is { } status)
            {
                ctx.ProblemDetails.Extensions["code"] = status switch
                {
                    401 => "unauthenticated",
                    403 => "forbidden",
                    404 => "not_found",
                    405 => "method_not_allowed",
                    _ => "error",
                };
            }
        });

    // ---------- Request logging ----------

    /// <summary>One structured line per request (method, path, status, duration, user). No bodies are logged.</summary>
    public static IApplicationBuilder UseApiRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnostics, http) =>
            {
                if (http.User.GetUserId() is { } userId)
                {
                    diagnostics.Set("UserId", userId);
                }
            };
            options.GetLevel = (http, _, ex) =>
                ex is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
                : http.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
        });

    // ---------- OpenAPI ----------

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi("v1", options => options.AddDocumentTransformer<ApiDocumentTransformer>());

    /// <summary>Swagger UI in Development, or anywhere OpenApi:EnableUi is true (never on public production).</summary>
    public static WebApplication UseApiOpenApiUi(this WebApplication app)
    {
        if (IsOpenApiEnabled(app))
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(OpenApiDocumentPath, "Community SOS API v1");
                options.RoutePrefix = "swagger";
            });
        }

        return app;
    }

    public static WebApplication MapApiOpenApiDocument(this WebApplication app)
    {
        if (IsOpenApiEnabled(app))
        {
            app.MapOpenApi().AllowAnonymous().DisableRateLimiting();
        }

        return app;
    }

    private static bool IsOpenApiEnabled(WebApplication app) =>
        app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:EnableUi");

    /// <summary>Document metadata + JWT bearer scheme so "Authorize" works in Swagger UI.</summary>
    private sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
    {
        private const string SchemeId = "Bearer";

        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Info = new OpenApiInfo
            {
                Title = "Community SOS API",
                Version = "v1",
                Description = "REST API for the Community SOS mobile app. Errors use RFC 7807 problem+json with a stable `code`.",
            };

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token from POST /api/v1/auth/login.",
            };

            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeId, document)] = [],
            });

            return Task.CompletedTask;
        }
    }
}
