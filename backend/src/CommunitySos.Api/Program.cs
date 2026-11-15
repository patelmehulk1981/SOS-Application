using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using CommunitySos.Api.Errors;
using CommunitySos.Api.Extensions;
using CommunitySos.Api.Filters;
using CommunitySos.Api.Middleware;
using CommunitySos.Api.Security;
using CommunitySos.Application;
using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Features.Sos.Jobs;
using CommunitySos.Infrastructure;
using CommunitySos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging & telemetry ----------
builder.Services.AddSerilog(
    (services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "CommunitySos.Api"),
    writeToProviders: true); // lets the Azure Monitor (OpenTelemetry) provider receive the same logs

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

// ---------- Application layers ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>(); // must precede AddInfrastructure (TryAdd)
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SosOptions>()
    .Bind(builder.Configuration.GetSection(SosOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AlertOptions>()
    .Bind(builder.Configuration.GetSection(AlertOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RetentionOptions>()
    .Bind(builder.Configuration.GetSection(RetentionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---------- Web ----------
builder.Services.AddApiAuthorization();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddApiOpenApi();

builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<PolicyAcceptanceFilter>();
        options.Filters.Add<ValidationFilter>();
        options.Conventions.Add(new RouteTokenTransformerConvention(new KebabCaseRouteTransformer()));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddApiProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("sql", tags: ["ready"]);

// Azure App Service / Front Door terminate TLS and forward the client IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;
    if (builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies"))
    {
        // Only safe when the app is reachable exclusively through the platform's proxy (App Service access restrictions).
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

// ---------- One-off commands ----------
if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    app.Logger.LogInformation("Database is up to date");
    return;
}

// ---------- Recurring jobs ----------
try
{
    RecurringJobs.Register(app.Services.GetRequiredService<IJobScheduler>());
}
catch (Exception ex)
{
    // Job storage unavailable at boot must not take the API down; registration is retried on next start.
    app.Logger.LogError(ex, "Could not register recurring jobs");
}

// ---------- Pipeline ----------
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
// .NET 10 default: no middleware error log when GlobalExceptionHandler handled the exception
// (it logs 4xx at Information and 5xx at Error with the stack trace itself).
app.UseExceptionHandler();
app.UseStatusCodePages(); // ProblemDetails body for bare 401/403/404/405

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseApiRequestLogging();

app.UseApiOpenApiUi(); // before auth: the UI is static; the JSON document is an anonymous endpoint

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous()
    .DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .AllowAnonymous()
    .DisableRateLimiting();

app.MapApiOpenApiDocument();
app.MapControllers();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
