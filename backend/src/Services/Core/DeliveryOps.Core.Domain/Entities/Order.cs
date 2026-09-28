using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class Order : Entity
{
    private Order() { }

    private Order(
        Guid businessId,
        Guid branchId,
        string externalId,
        string customerName,
        string customerPhone,
        string deliveryAddress,
        OrderSource source,
        decimal totalAmount,
        string creationIdempotencyKey,
        string creationRequestHash,
        double? deliveryLatitude,
        double? deliveryLongitude,
        string? deliveryInstructions,
        DeliveryLocationSource deliveryLocationSource,
        DeliveryLocationAccuracy deliveryLocationAccuracy,
        DeliveryFulfillmentType deliveryFulfillment)
    {
        BusinessId = businessId;
        BranchId = branchId;
        ExternalId = externalId;
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        DeliveryAddress = deliveryAddress;
        DeliveryLatitude = deliveryLatitude;
        DeliveryLongitude = deliveryLongitude;
        DeliveryInstructions = NormalizeOptional(deliveryInstructions);
        DeliveryLocationSource = deliveryLocationSource;
        DeliveryLocationAccuracy = deliveryLocationAccuracy;
        DeliveryFulfillment = deliveryFulfillment;
        Source = source;
        TotalAmount = totalAmount;
        CreationIdempotencyKey = creationIdempotencyKey;
        CreationRequestHash = creationRequestHash;
        Status = OrderStatus.New;
    }

    public Guid BusinessId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? CourierId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string DeliveryAddress { get; private set; } = string.Empty;
    public double? DeliveryLatitude { get; private set; }
    public double? DeliveryLongitude { get; private set; }
    public string? DeliveryInstructions { get; private set; }
    public DeliveryLocationSource DeliveryLocationSource { get; private set; }
    public DeliveryLocationAccuracy DeliveryLocationAccuracy { get; private set; }
    public DeliveryFulfillmentType DeliveryFulfillment { get; private set; }
    public string CustomerSearchTokens { get; private set; } = string.Empty;
    public DateTimeOffset? PiiAnonymizedAtUtc { get; private set; }
    public OrderSource Source { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public string? CancellationReason { get; private set; }
    public string? DeliveryFailureReason { get; private set; }
    public string CreationIdempotencyKey { get; private init; } = string.Empty;
    public string CreationRequestHash { get; private init; } = string.Empty;
    public uint Version { get; private set; }
    public ICollection<OrderStatusHistory> StatusHistory { get; private set; } = [];
    public IReadOnlyList<OrderStatus> AllowedNextStatuses => GetAllowedTransitions(Status);
    public static IReadOnlyList<OrderStatus> GetAllowedTransitions(OrderStatus status) =>
        AllowedTransitions.TryGetValue(status, out OrderStatus[]? values) ? values : [];

    public static Order Create(
        Guid businessId,
        Guid branchId,
        string externalId,
        string customerName,
        string customerPhone,
        string deliveryAddress,
        OrderSource source,
        decimal totalAmount,
        string creationIdempotencyKey,
        string creationRequestHash,
        Guid changedByUserId,
        double? deliveryLatitude = null,
        double? deliveryLongitude = null,
        string? deliveryInstructions = null,
        DeliveryLocationSource deliveryLocationSource = DeliveryLocationSource.Unknown,
        DeliveryLocationAccuracy deliveryLocationAccuracy = DeliveryLocationAccuracy.Unknown,
        DeliveryFulfillmentType deliveryFulfillment = DeliveryFulfillmentType.MerchantCourier)
    {
        if (businessId == Guid.Empty || branchId == Guid.Empty) throw new ArgumentException("Business and branch are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerPhone);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryAddress);
        if (totalAmount < 0) throw new ArgumentOutOfRangeException(nameof(totalAmount));
        ArgumentException.ThrowIfNullOrWhiteSpace(creationIdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(creationRequestHash);
        ValidateDeliveryLocation(deliveryLatitude, deliveryLongitude, deliveryLocationSource,
            deliveryLocationAccuracy);
        Order order = new(businessId, branchId, externalId.Trim(), customerName.Trim(), customerPhone.Trim(), deliveryAddress.Trim(),
            source, totalAmount, creationIdempotencyKey.Trim(), creationRequestHash, deliveryLatitude,
            deliveryLongitude, deliveryInstructions, deliveryLocationSource, deliveryLocationAccuracy,
            deliveryFulfillment);
        order.StatusHistory.Add(OrderStatusHistory.Create(order.Id, null, OrderStatus.New, changedByUserId));
        return order;
    }

    public void UpdateCustomer(string customerName, string customerPhone, string deliveryAddress, decimal totalAmount,
        double? deliveryLatitude = null, double? deliveryLongitude = null, string? deliveryInstructions = null,
        DeliveryLocationSource deliveryLocationSource = DeliveryLocationSource.Unknown,
        DeliveryLocationAccuracy deliveryLocationAccuracy = DeliveryLocationAccuracy.Unknown)
    {
        if (Status is OrderStatus.PickedUp or OrderStatus.OnTheWay or OrderStatus.DeliveryFailed or
            OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned)
            throw new InvalidOperationException("Orders cannot be edited after pickup.");
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerPhone);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryAddress);
        if (totalAmount < 0) throw new ArgumentOutOfRangeException(nameof(totalAmount));
        ValidateDeliveryLocation(deliveryLatitude, deliveryLongitude, deliveryLocationSource,
            deliveryLocationAccuracy);
        bool addressChanged = !string.Equals(DeliveryAddress, deliveryAddress.Trim(), StringComparison.Ordinal);
        CustomerName = customerName.Trim();
        CustomerPhone = customerPhone.Trim();
        DeliveryAddress = deliveryAddress.Trim();
        if (deliveryLatitude.HasValue || addressChanged)
        {
            DeliveryLatitude = deliveryLatitude;
            DeliveryLongitude = deliveryLongitude;
            DeliveryInstructions = NormalizeOptional(deliveryInstructions);
            DeliveryLocationSource = deliveryLocationSource;
            DeliveryLocationAccuracy = deliveryLocationAccuracy;
        }
        else if (deliveryInstructions is not null)
        {
            DeliveryInstructions = NormalizeOptional(deliveryInstructions);
        }
        TotalAmount = totalAmount;
        MarkAsUpdated();
    }

    public void SetCustomerSearchTokens(string tokens) => CustomerSearchTokens = tokens;

    public void AnonymizePii(DateTimeOffset anonymizedAtUtc)
    {
        if (Status is not (OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned))
            throw new InvalidOperationException("Only terminal orders can be anonymized.");
        if (PiiAnonymizedAtUtc.HasValue) return;
        CustomerName = "Anonim Müşteri";
        CustomerPhone = "-";
        DeliveryAddress = "[silindi]";
        DeliveryLatitude = null;
        DeliveryLongitude = null;
        DeliveryInstructions = null;
        DeliveryLocationSource = DeliveryLocationSource.Unknown;
        DeliveryLocationAccuracy = DeliveryLocationAccuracy.Unknown;
        CustomerSearchTokens = string.Empty;
        PiiAnonymizedAtUtc = anonymizedAtUtc;
        MarkAsUpdated();
    }

    private static void ValidateDeliveryLocation(double? latitude, double? longitude,
        DeliveryLocationSource source, DeliveryLocationAccuracy accuracy)
    {
        if (latitude.HasValue != longitude.HasValue)
            throw new ArgumentException("Delivery latitude and longitude must be supplied together.");
        if (!latitude.HasValue)
        {
            if (source != DeliveryLocationSource.Unknown || accuracy != DeliveryLocationAccuracy.Unknown)
                throw new ArgumentException("Delivery location metadata requires coordinates.");
            return;
        }
        if (!double.IsFinite(latitude.Value) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        if (!double.IsFinite(longitude!.Value) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude));
        if (latitude == 0 && longitude == 0)
            throw new ArgumentException("Delivery coordinates cannot be the null island coordinate.");
        if (source == DeliveryLocationSource.Unknown || accuracy == DeliveryLocationAccuracy.Unknown)
            throw new ArgumentException("Delivery coordinate source and accuracy are required.");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void AssignCourier(Guid courierId, Guid changedByUserId)
    {
        if (courierId == Guid.Empty) throw new ArgumentException("Courier is required.", nameof(courierId));
        if (DeliveryFulfillment != DeliveryFulfillmentType.MerchantCourier)
            throw new InvalidOperationException("Only merchant courier orders can be assigned to a courier.");
        if (CourierId.HasValue) throw new InvalidOperationException("Order has already been assigned to a courier.");
        EnsureTransitionAllowed(OrderStatus.Assigned);
        CourierId = courierId;
        ChangeStatus(OrderStatus.Assigned, changedByUserId);
    }

    public Guid ReassignCourier(Guid courierId)
    {
        if (Status != OrderStatus.Assigned)
            throw new InvalidOperationException("Only an order that has not been picked up can be reassigned.");
        if (!CourierId.HasValue) throw new InvalidOperationException("Order has no assigned courier.");
        if (courierId == Guid.Empty) throw new ArgumentException("Courier is required.", nameof(courierId));
        if (CourierId == courierId) return courierId;
        Guid previousCourierId = CourierId.Value;
        CourierId = courierId;
        MarkAsUpdated();
        return previousCourierId;
    }

    public void Cancel(string? reason, Guid changedByUserId)
    {
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ChangeStatus(OrderStatus.Cancelled, changedByUserId);
    }

    public void ReportDeliveryFailure(string reason, Guid changedByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        DeliveryFailureReason = reason.Trim();
        ChangeStatus(OrderStatus.DeliveryFailed, changedByUserId);
    }

    public void ChangeStatus(OrderStatus next, Guid changedByUserId)
    {
        EnsureTransitionAllowed(next);
        if (next == OrderStatus.Assigned && !CourierId.HasValue)
            throw new InvalidOperationException("An order can only become Assigned through courier assignment.");
        OrderStatus previous = Status;
        Status = next;
        StatusHistory.Add(OrderStatusHistory.Create(Id, previous, next, changedByUserId));
        MarkAsUpdated();
    }

    private void EnsureTransitionAllowed(OrderStatus next)
    {
        if (!AllowedTransitions.TryGetValue(Status, out OrderStatus[]? allowed) || !allowed.Contains(next))
            throw new InvalidOperationException($"Order cannot transition from {Status} to {next}.");
    }

    public void ApplyProviderDeliveryStatus(OrderStatus next, Guid changedByUserId)
    {
        if (DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier)
            throw new InvalidOperationException("Merchant courier orders must use the standard delivery workflow.");
        if (next is not (OrderStatus.OnTheWay or OrderStatus.Delivered))
            throw new ArgumentOutOfRangeException(nameof(next));
        if (Status == OrderStatus.New) SetProviderStatus(OrderStatus.Confirmed, changedByUserId);
        // WaitingForCourier is where the merchant marks a provider-courier order ready for the provider's courier.
        if (Status is OrderStatus.Confirmed or OrderStatus.WaitingForCourier)
            SetProviderStatus(OrderStatus.OnTheWay, changedByUserId);
        if (next == OrderStatus.Delivered && Status == OrderStatus.OnTheWay)
            SetProviderStatus(OrderStatus.Delivered, changedByUserId);
        if (Status != next && !(next == OrderStatus.OnTheWay && Status == OrderStatus.Delivered))
            throw new InvalidOperationException($"Provider order cannot transition from {Status} to {next}.");
    }

    private void SetProviderStatus(OrderStatus next, Guid changedByUserId)
    {
        OrderStatus previous = Status;
        Status = next;
        StatusHistory.Add(OrderStatusHistory.Create(Id, previous, next, changedByUserId));
        MarkAsUpdated();
    }

    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedTransitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.New] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.WaitingForCourier, OrderStatus.Assigned, OrderStatus.Cancelled],
            [OrderStatus.WaitingForCourier] = [OrderStatus.Assigned, OrderStatus.Cancelled],
            [OrderStatus.Assigned] = [OrderStatus.PickedUp, OrderStatus.Cancelled],
            [OrderStatus.PickedUp] = [OrderStatus.OnTheWay, OrderStatus.Returned],
            [OrderStatus.OnTheWay] = [OrderStatus.Delivered, OrderStatus.DeliveryFailed, OrderStatus.Returned],
            [OrderStatus.DeliveryFailed] = [OrderStatus.OnTheWay, OrderStatus.Returned],
            [OrderStatus.Returned] = [] ,
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = []
        };
}
