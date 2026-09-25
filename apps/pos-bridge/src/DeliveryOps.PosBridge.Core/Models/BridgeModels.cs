namespace DeliveryOps.PosBridge.Core.Models;

public sealed record BridgeRuntimeSettings(string WebhookUrl, string Secret, string InboxDirectory, int PollIntervalSeconds = 5)
{
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            errors.Add("Webhook URL must use HTTPS; HTTP is allowed only for localhost.");
        if (string.IsNullOrWhiteSpace(Secret)) errors.Add("Webhook secret is required.");
        if (string.IsNullOrWhiteSpace(InboxDirectory)) errors.Add("Inbox directory is required.");
        if (PollIntervalSeconds is < 1 or > 300) errors.Add("Poll interval must be between 1 and 300 seconds.");
        return errors;
    }
}

public enum BridgeLogLevel { Information, Success, Warning, Error }
public sealed record BridgeLogEntry(DateTimeOffset Timestamp, BridgeLogLevel Level, string Message, string? ExternalOrderId = null);
public sealed record QueuedOrder(string FilePath, PosOrder? Order, string? ParseError);
public sealed record OrderSendResult(bool Success, bool Retryable, Guid? OrderId, bool Duplicate, string? Error)
{
    public static OrderSendResult Sent(Guid? orderId, bool duplicate) => new(true, false, orderId, duplicate, null);
    public static OrderSendResult Failed(string error, bool retryable) => new(false, retryable, null, false, error);
}
public sealed record ProcessingCycleResult(int Processed, int Retrying, int Rejected);
