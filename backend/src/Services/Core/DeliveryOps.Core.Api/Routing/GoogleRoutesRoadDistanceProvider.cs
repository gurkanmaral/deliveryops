using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DeliveryOps.Core.Queries.Dispatch;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Core.Api.Routing;

public sealed class RoadRoutingOptions
{
    public bool Enabled { get; init; }
    public string GoogleApiKey { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 5;
    public int MaxElementsPerRequest { get; init; } = 100;
}

public sealed class GoogleRoutesRoadDistanceProvider(
    HttpClient client,
    IOptions<RoadRoutingOptions> options,
    ILogger<GoogleRoutesRoadDistanceProvider> logger) : IRoadRouteDistanceProvider
{
    private const string FieldMask = "originIndex,destinationIndex,status,condition,distanceMeters,duration";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RoadRoutingOptions _options = options.Value;

    public bool IsAvailable => _options.Enabled && !string.IsNullOrWhiteSpace(_options.GoogleApiKey);
    public string ProviderName => "Google Routes";
    public int MaxElementsPerRequest => _options.MaxElementsPerRequest;

    public async Task<IReadOnlyList<RoadRouteElement>> CalculateMatrixAsync(
        IReadOnlyList<RoadRoutePoint> origins,
        IReadOnlyList<RoadRoutePoint> destinations,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable || origins.Count == 0 || destinations.Count == 0) return [];
        if ((long)origins.Count * destinations.Count > _options.MaxElementsPerRequest)
            throw new ArgumentOutOfRangeException(nameof(origins), "Road route matrix element limit exceeded.");

        object payload = new
        {
            origins = origins.Select(point => new { waypoint = Waypoint(point) }),
            destinations = destinations.Select(point => new { waypoint = Waypoint(point) }),
            travelMode = "TWO_WHEELER",
            routingPreference = "TRAFFIC_AWARE"
        };
        using HttpRequestMessage request = new(HttpMethod.Post, "distanceMatrix/v2:computeRouteMatrix")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Goog-Api-Key", _options.GoogleApiKey);
        request.Headers.Add("X-Goog-FieldMask", FieldMask);

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Road routing provider returned HTTP {StatusCode}; route clustering will fail closed.",
                    (int)response.StatusCode);
                return [];
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            RouteMatrixElementDto[]? elements = await JsonSerializer.DeserializeAsync<RouteMatrixElementDto[]>(stream,
                JsonOptions, cancellationToken);
            return elements?.Where(element => element.Condition == "ROUTE_EXISTS" &&
                                               element.Status?.Code is null or 0 &&
                                               element.DistanceMeters.HasValue && TryParseDuration(element.Duration, out _))
                .Select(element => new RoadRouteElement(element.OriginIndex, element.DestinationIndex,
                    element.DistanceMeters!.Value / 1000d,
                    TryParseDuration(element.Duration, out TimeSpan duration) ? duration : TimeSpan.Zero))
                .ToArray() ?? [];
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                          exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Road routing provider failed; route clustering will fail closed.");
            return [];
        }
    }

    private static object Waypoint(RoadRoutePoint point) => new
    {
        location = new { latLng = new { latitude = point.Latitude, longitude = point.Longitude } }
    };

    private static bool TryParseDuration(string? value, out TimeSpan duration)
    {
        double seconds = 0;
        bool parsed = value is not null && value.EndsWith('s') &&
                      double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                          out seconds);
        duration = parsed ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        return parsed;
    }

    private sealed record RouteMatrixElementDto(
        int OriginIndex,
        int DestinationIndex,
        RouteStatusDto? Status,
        string? Condition,
        int? DistanceMeters,
        string? Duration);

    private sealed record RouteStatusDto(int? Code);
}
