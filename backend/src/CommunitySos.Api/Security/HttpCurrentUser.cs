using CommunitySos.Api.Middleware;
using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Api.Security;

/// <summary>Caller identity from the validated JWT. Outside a request (background work) every value is null.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Context => accessor.HttpContext;

    public Guid? UserId => Context?.User.GetUserId();

    public UserRole? Role => Context?.User.GetRole();

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = Context?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public string? CorrelationId => Context is null ? null : CorrelationIdMiddleware.Get(Context);
}
