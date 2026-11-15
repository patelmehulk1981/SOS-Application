using System.Text.RegularExpressions;

namespace CommunitySos.Api.Extensions;

/// <summary>Turns [controller]/[action] tokens into kebab-case: "SosRequests" → "sos-requests".</summary>
public sealed partial class KebabCaseRouteTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) =>
        value is null ? null : WordBoundary().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundary();
}
