using CommunitySos.Api.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace CommunitySos.Api.Filters;

/// <summary>
/// Runs the registered FluentValidation validator (if any) for every action argument and short-circuits
/// with a 400 ProblemDetails, so controllers only ever see valid requests.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services, ProblemDetailsFactory problemDetailsFactory) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid)
            {
                continue;
            }

            var problem = problemDetailsFactory.CreateProblemDetails(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                title: "Validation failed",
                detail: "One or more fields are invalid.");
            problem.Extensions["code"] = "validation_failed";
            problem.Extensions["errors"] = ValidationErrors.From(result.Errors);

            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await next();
    }
}
