using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Infrastructure.Geo;

/// <summary>Bound from "GoogleMaps". The server-side key must be restricted to the Geocoding API (and to the API's outbound IPs).</summary>
public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    public string? ApiKey { get; init; }

    public string Language { get; init; } = "en";

    public string Region { get; init; } = "in";
}

/// <summary>
/// Google Geocoding API reverse lookup. Best effort: any failure returns null and the SOS simply has no
/// address (the map pin is still exact). Coordinates are never written to logs.
/// </summary>
public sealed class GoogleGeocoder(HttpClient http, IOptions<GoogleMapsOptions> options, ILogger<GoogleGeocoder> logger) : IGeocoder
{
    public async Task<string?> ReverseGeocodeAsync(GeoCoordinate coordinate, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"maps/api/geocode/json?latlng={coordinate.Latitude},{coordinate.Longitude}&language={o.Language}&region={o.Region}&key={Uri.EscapeDataString(o.ApiKey!)}");

        try
        {
            var response = await http.GetFromJsonAsync<GeocodeResponse>(url, cancellationToken);
            if (response is { Status: "OK", Results: [var first, ..] } && !string.IsNullOrWhiteSpace(first.FormattedAddress))
            {
                return first.FormattedAddress;
            }

            logger.LogWarning("Reverse geocoding returned {Status}", response?.Status ?? "no body");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reverse geocoding failed");
            return null;
        }
    }

    private sealed record GeocodeResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("results")] IReadOnlyList<GeocodeResult>? Results);

    private sealed record GeocodeResult([property: JsonPropertyName("formatted_address")] string? FormattedAddress);
}

/// <summary>Used when no API key is configured (local development, tests).</summary>
public sealed class NullGeocoder : IGeocoder
{
    public Task<string?> ReverseGeocodeAsync(GeoCoordinate coordinate, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
