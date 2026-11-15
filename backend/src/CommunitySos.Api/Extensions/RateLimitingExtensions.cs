using System.Globalization;
using System.Threading.RateLimiting;
using CommunitySos.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CommunitySos.Api.Extensions;

/// <summary>Policy names for [EnableRateLimiting(...)] on controllers/actions.</summary>
public static class RateLimitPolicies
{
    /// <summary>Login / token refresh, per IP (protects Firebase quota and brute force).</summary>
    public const string Auth = "auth";

    /// <summary>Public invitation-code lookup, per IP (prevents code enumeration).</summary>
    public const string InviteLookup = "invite-lookup";

    /// <summary>Raising an SOS, per user (stops floods; idempotent retries are cheap anyway).</summary>
    public const string SosCreate = "sos-create";

    /// <summary>Background location pings, per user.</summary>
    public const string LocationUpdate = "location-update";

    /// <summary>Background location reports, per location key (anonymous: no user claim).</summary>
    public const string LocationBackground = "location-background";
}

/// <summary>Bound from "RateLimiting". Defaults are production values; tune per environment (e.g. load tests).</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int GlobalPerMinute { get; init; } = 300;

    public int AuthPerMinute { get; init; } = 10;

    public int InviteLookupPerMinute { get; init; } = 20;

    public int SosCreatePer10Minutes { get; init; } = 5;

    public int LocationUpdatesPerMinute { get; init; } = 6;
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(UserOrIp(ctx), _ => Window(limits.GlobalPerMinute, TimeSpan.FromMinutes(1))));

            options.AddPolicy(RateLimitPolicies.Auth, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(Ip(ctx), _ => Window(limits.AuthPerMinute, TimeSpan.FromMinutes(1))));

            options.AddPolicy(RateLimitPolicies.InviteLookup, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(Ip(ctx), _ => Window(limits.InviteLookupPerMinute, TimeSpan.FromMinutes(1))));

            options.AddPolicy(RateLimitPolicies.SosCreate, ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(UserOrIp(ctx), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = limits.SosCreatePer10Minutes,
                    Window = TimeSpan.FromMinutes(10),
                    SegmentsPerWindow = 10,
                    QueueLimit = 0,
                }));

            options.AddPolicy(RateLimitPolicies.LocationUpdate, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(UserOrIp(ctx), _ => Window(limits.LocationUpdatesPerMinute, TimeSpan.FromMinutes(1))));

            // Keyed by the presented key (a prefix is unique enough for 256-bit keys); without one, by IP.
            options.AddPolicy(RateLimitPolicies.LocationBackground, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(LocationKeyOrIp(ctx), _ => Window(limits.LocationUpdatesPerMinute, TimeSpan.FromMinutes(1))));
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string UserOrIp(HttpContext ctx) =>
        ctx.User.GetUserId() is { } userId ? $"u:{userId:N}" : Ip(ctx);

    private static string Ip(HttpContext ctx) => $"ip:{ctx.Connection.RemoteIpAddress}";

    private static string LocationKeyOrIp(HttpContext ctx)
    {
        var key = ctx.Request.Headers[Controllers.LocationController.KeyHeader].ToString();
        return key.Length >= 24 ? $"lk:{key[..24]}" : Ip(ctx);
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = "Please wait a moment and try again.",
            Type = "https://httpstatuses.io/429",
        };
        problem.Extensions["code"] = "rate_limited";

        var writer = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await writer.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
    }
}
