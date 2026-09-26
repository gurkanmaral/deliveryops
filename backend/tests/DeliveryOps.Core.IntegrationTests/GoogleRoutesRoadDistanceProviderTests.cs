using System.Net;
using System.Text;
using DeliveryOps.Core.Api.Routing;
using DeliveryOps.Core.Queries.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeliveryOps.Core.IntegrationTests;

public sealed class GoogleRoutesRoadDistanceProviderTests
{
    [Fact]
    public async Task CalculateMatrixAsync_UsesTwoWheelerRoadDistance()
    {
        const string responseJson = """
            [{"originIndex":0,"destinationIndex":0,"status":{},"condition":"ROUTE_EXISTS","distanceMeters":3200,"duration":"420s"}]
            """;
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        GoogleRoutesRoadDistanceProvider provider = CreateProvider(handler);

        IReadOnlyList<RoadRouteElement> result = await provider.CalculateMatrixAsync(
            [new RoadRoutePoint(40.99, 29.03)], [new RoadRoutePoint(40.991, 29.031)],
            CancellationToken.None);

        RoadRouteElement route = Assert.Single(result);
        Assert.Equal(3.2, route.DistanceKm);
        Assert.Equal(TimeSpan.FromMinutes(7), route.Duration);
        Assert.Contains("\"travelMode\":\"TWO_WHEELER\"", handler.RequestBody);
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Contains("distanceMeters", handler.FieldMask);
    }

    [Fact]
    public async Task CalculateMatrixAsync_FailsClosedWhenProviderIsUnavailable()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        GoogleRoutesRoadDistanceProvider provider = CreateProvider(handler);

        IReadOnlyList<RoadRouteElement> result = await provider.CalculateMatrixAsync(
            [new RoadRoutePoint(40.99, 29.03)], [new RoadRoutePoint(40.991, 29.031)],
            CancellationToken.None);

        Assert.Empty(result);
    }

    private static GoogleRoutesRoadDistanceProvider CreateProvider(HttpMessageHandler handler)
    {
        HttpClient client = new(handler) { BaseAddress = new Uri("https://routes.googleapis.com/") };
        return new GoogleRoutesRoadDistanceProvider(client, Options.Create(new RoadRoutingOptions
        {
            Enabled = true,
            GoogleApiKey = "test-key",
            MaxElementsPerRequest = 100
        }), NullLogger<GoogleRoutesRoadDistanceProvider>.Instance);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;
        public string ApiKey { get; private set; } = string.Empty;
        public string FieldMask { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.GetValues("X-Goog-Api-Key").Single();
            FieldMask = request.Headers.GetValues("X-Goog-FieldMask").Single();
            return responseFactory(request);
        }
    }
}
