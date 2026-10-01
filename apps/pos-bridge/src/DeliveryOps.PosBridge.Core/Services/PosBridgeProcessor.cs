using DeliveryOps.PosBridge.Core.Abstractions;
using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Core.Services;

public sealed class PosBridgeProcessor(IOrderQueue queue, IOrderSender sender)
{
    public event EventHandler<BridgeLogEntry>? LogReceived;

    public async Task<ProcessingCycleResult> ProcessOnceAsync(BridgeRuntimeSettings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> settingsErrors = settings.Validate();
        if (settingsErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", settingsErrors));

        int processed = 0, retrying = 0, rejected = 0;
        IReadOnlyList<QueuedOrder> orders = await queue.ReadPendingAsync(settings.InboxDirectory, 25, cancellationToken);
        foreach (QueuedOrder queued in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queued.Order is null)
            {
                await queue.RejectAsync(queued, queued.ParseError ?? "Invalid order document.", cancellationToken);
                rejected++;
                Log(BridgeLogLevel.Error, $"Invalid file moved to failed: {Path.GetFileName(queued.FilePath)}");
                continue;
            }
            IReadOnlyList<string> validationErrors = queued.Order.Validate();
            if (validationErrors.Count > 0)
            {
                await queue.RejectAsync(queued, string.Join(" ", validationErrors), cancellationToken);
                rejected++;
                Log(BridgeLogLevel.Error, "Order validation failed and file was moved to failed.", queued.Order.ExternalOrderId);
                continue;
            }

            OrderSendResult result = await sender.SendAsync(settings, queued.Order, cancellationToken);
            if (result.Success)
            {
                await queue.CompleteAsync(queued, cancellationToken);
                processed++;
                Log(BridgeLogLevel.Success, result.Duplicate ? "Order already existed; file archived." : "Order sent successfully.", queued.Order.ExternalOrderId);
            }
            else if (result.Retryable)
            {
                retrying++;
                Log(BridgeLogLevel.Warning, $"Temporary failure; order will retry: {result.Error}", queued.Order.ExternalOrderId);
                // Keep the inbox order: a payment queued behind its order must not reach DeliveryOps first,
                // and while the connection is down every later file would fail the same way.
                break;
            }
            else
            {
                await queue.RejectAsync(queued, result.Error ?? "Webhook rejected the order.", cancellationToken);
                rejected++;
                Log(BridgeLogLevel.Error, $"Order rejected: {result.Error}", queued.Order.ExternalOrderId);
            }
        }
        return new ProcessingCycleResult(processed, retrying, rejected);
    }

    private void Log(BridgeLogLevel level, string message, string? externalOrderId = null) =>
        LogReceived?.Invoke(this, new BridgeLogEntry(DateTimeOffset.Now, level, message, externalOrderId));
}
