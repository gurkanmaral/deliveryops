using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Core.Abstractions;

public interface IOrderSender
{
    Task<OrderSendResult> SendAsync(BridgeRuntimeSettings settings, PosOrder order, CancellationToken cancellationToken);
}
