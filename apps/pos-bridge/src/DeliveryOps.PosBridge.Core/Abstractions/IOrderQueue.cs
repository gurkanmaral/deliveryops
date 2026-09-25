using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Core.Abstractions;

public interface IOrderQueue
{
    Task<IReadOnlyList<QueuedOrder>> ReadPendingAsync(string inboxDirectory, int take, CancellationToken cancellationToken);
    Task CompleteAsync(QueuedOrder order, CancellationToken cancellationToken);
    Task RejectAsync(QueuedOrder order, string reason, CancellationToken cancellationToken);
}
