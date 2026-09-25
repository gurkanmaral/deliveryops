using System.Net;

namespace DeliveryOps.Core.Api.Operations;

internal static class OutboxHttpPolicy
{
    public static bool IsRetryable(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests ||
        (int)statusCode is 425 or >= 500;
}
