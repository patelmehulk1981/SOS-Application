using System.Text.Json;
using FluentValidation.Results;

namespace CommunitySos.Api.Errors;

internal static class ValidationErrors
{
    /// <summary>Groups FluentValidation failures by camelCase property path ("location.latitude").</summary>
    public static IDictionary<string, string[]> From(IEnumerable<ValidationFailure> failures) =>
        failures
            .GroupBy(f => ToCamelPath(f.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);

    private static string ToCamelPath(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
