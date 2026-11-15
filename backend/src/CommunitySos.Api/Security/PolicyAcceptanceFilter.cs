using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace CommunitySos.Api.Security;

/// <summary>
/// Marks endpoints a user may call before accepting the current Privacy Notice version
/// (reading/accepting the notice, withdrawing consent, exporting/deleting data, logging out).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWithoutCurrentPolicyAttribute : Attribute;

/// <summary>
/// Global gate (DPDP s.5 / GDPR Art. 13): an authenticated user whose token carries an older policy
/// version gets 403 "policy.acceptance_required" until they accept the current notice.
/// </summary>
public sealed class PolicyAcceptanceFilter(IOptionsMonitor<AppOptions> appOptions, ProblemDetailsFactory problemDetailsFactory)
    : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true
            || metadata.OfType<IAllowAnonymous>().Any()
            || metadata.OfType<AllowWithoutCurrentPolicyAttribute>().Any())
        {
            return Task.CompletedTask;
        }

        var accepted = context.HttpContext.User.FindFirst(AppClaimTypes.PolicyVersion)?.Value;
        if (string.Equals(accepted, appOptions.CurrentValue.PolicyVersion, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var problem = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            title: "Privacy Notice acceptance required",
            detail: "Please review and accept the updated Privacy Notice to continue.");
        problem.Extensions["code"] = "policy.acceptance_required";
        problem.Extensions["policyVersion"] = appOptions.CurrentValue.PolicyVersion;

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
        return Task.CompletedTask;
    }
}
