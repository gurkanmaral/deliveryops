using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class OrderStatusHistory : Entity
{
    private OrderStatusHistory() { }

    public Guid OrderId { get; private init; }
    public Order Order { get; private init; } = null!;
    public OrderStatus? PreviousStatus { get; private init; }
    public OrderStatus Status { get; private init; }
    public Guid ChangedByUserId { get; private init; }

    public static OrderStatusHistory Create(Guid orderId, OrderStatus? previous, OrderStatus status, Guid userId) =>
        new() { OrderId = orderId, PreviousStatus = previous, Status = status, ChangedByUserId = userId };
}
