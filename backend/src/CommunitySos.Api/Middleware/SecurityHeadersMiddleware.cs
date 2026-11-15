namespace CommunitySos.Api.Middleware;

/// <summary>
/// Defensive headers for a JSON API. API responses can contain location and emergency details,
/// so they are never cached by intermediaries or the device's HTTP cache.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                if (string.IsNullOrEmpty(headers.CacheControl))
                {
                    headers.CacheControl = "no-store"; // endpoints may opt in to caching explicitly
                }

                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
