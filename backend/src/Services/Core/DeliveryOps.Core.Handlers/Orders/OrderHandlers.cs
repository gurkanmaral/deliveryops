using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Dispatch;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Orders;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DeliveryOps.Core.Handlers.Orders;

public sealed class GetOrdersHandler(ICoreDbContext context, IRequestContext requestContext, IOrderPiiProtector orderPiiProtector)
    : IRequestHandler<GetOrdersQuery, Result<PagedResponse<OrderResponse>>>
{
    public async Task<Result<PagedResponse<OrderResponse>>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        if (request.CreatedFrom.HasValue && request.CreatedTo.HasValue && request.CreatedFrom > request.CreatedTo)
            return Result<PagedResponse<OrderResponse>>.Failure(HandlerErrors.Validation("Başlangıç tarihi bitiş tarihinden sonra olamaz."));
        if (request.MinAmount.HasValue && request.MaxAmount.HasValue && request.MinAmount > request.MaxAmount)
            return Result<PagedResponse<OrderResponse>>.Failure(HandlerErrors.Validation("Minimum tutar maksimum tutardan büyük olamaz."));
        if (request.MinAmount < 0 || request.MaxAmount < 0)
            return Result<PagedResponse<OrderResponse>>.Failure(HandlerErrors.Validation("Sipariş tutarı negatif olamaz."));

        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && businessId is null) return Result<PagedResponse<OrderResponse>>.Failure(HandlerErrors.Forbidden);
        IQueryable<Order> query = context.Orders.AsNoTracking();
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        Guid? branchId = requestContext.BranchId ?? request.BranchId;
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId.Value);
        if (request.CourierId.HasValue) query = query.Where(x => x.CourierId == request.CourierId.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (request.Source.HasValue) query = query.Where(x => x.Source == request.Source.Value);
        if (request.CreatedFrom.HasValue)
        {
            DateTimeOffset from = new(request.CreatedFrom.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAtUtc >= from);
        }
        if (request.CreatedTo.HasValue)
        {
            DateTimeOffset until = new(request.CreatedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAtUtc < until);
        }
        if (request.MinAmount.HasValue) query = query.Where(x => x.TotalAmount >= request.MinAmount.Value);
        if (request.MaxAmount.HasValue) query = query.Where(x => x.TotalAmount <= request.MaxAmount.Value);
        if (requestContext.CourierId.HasValue)
        {
            Guid courierId = requestContext.CourierId.Value;
            query = query.Where(x => x.CourierId == courierId);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string search = request.Search.Trim();
            string searchToken = orderPiiProtector.CreateSearchToken(search);
            query = query.Where(x => x.CustomerSearchTokens.Contains(searchToken) || x.ExternalId.Contains(search));
        }
        query = request.Sort.Trim().ToLowerInvariant() switch
        {
            "created" => query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "amount" => query.OrderBy(x => x.TotalAmount).ThenBy(x => x.Id),
            "-amount" => query.OrderByDescending(x => x.TotalAmount).ThenByDescending(x => x.Id),
            "status" => query.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "-status" => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "source" => query.OrderBy(x => x.Source).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "-source" => query.OrderByDescending(x => x.Source).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        };
        PagedResponse<OrderResponse> response = await query.Select(x => new OrderResponse(x.Id, x.BusinessId, x.BranchId, x.CourierId, x.ExternalId, x.CustomerName, x.CustomerPhone, x.DeliveryAddress, x.Source, x.Status, x.TotalAmount, x.Currency, x.CreatedAtUtc, x.CancellationReason, x.DeliveryFailureReason, Array.Empty<OrderStatus>(), null, 0, null, null, x.DeliveryLatitude, x.DeliveryLongitude, x.DeliveryInstructions, x.DeliveryLocationSource, x.DeliveryLocationAccuracy, x.DeliveryFulfillment))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        Guid[] orderIds = response.Items.Select(x => x.Id).ToArray();
        Dictionary<Guid, OrderDispatchState> dispatchStates = await context.OrderDispatchStates.AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId)).ToDictionaryAsync(x => x.OrderId, cancellationToken);
        response = response with { Items = response.Items.Select(x => GetOrderHandler.ApplyDispatchState(
            x with { AllowedNextStatuses = Order.GetAllowedTransitions(x.Status) }, dispatchStates.GetValueOrDefault(x.Id))).ToList() };
        return Result<PagedResponse<OrderResponse>>.Success(response);
    }
}

public sealed class GetOrderHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetOrderQuery, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (requestContext.CourierId.HasValue && order.CourierId != requestContext.CourierId)
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        OrderDispatchState? dispatchState = await context.OrderDispatchStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);
        return Result<OrderResponse>.Success(ApplyDispatchState(OrderMapper.Map(order), dispatchState));
    }

    public static OrderResponse ApplyDispatchState(OrderResponse response, OrderDispatchState? state) => state is null
        ? response
        : response with { DispatchStatus = state.Status, DispatchAttemptCount = state.AttemptCount,
            DispatchLastReason = state.LastReason, DispatchNextAttemptAtUtc = state.NextAttemptAtUtc };
}

public sealed class CreateOrderHandler(ICoreDbContext context, IRequestContext requestContext, IOrderOperationsNotifier notifier)
    : IRequestHandler<CreateOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsTrustedIntegration && !TenantAccess.CanAccess(requestContext, request.BusinessId))
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (!request.IsTrustedIntegration && requestContext.BranchId.HasValue && requestContext.BranchId != request.BranchId)
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (!await context.Branches.AnyAsync(x => x.Id == request.BranchId && x.BusinessId == request.BusinessId && x.IsActive, cancellationToken))
            return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Şube"));
        string idempotencyKey = request.IdempotencyKey.Trim();
        string requestHash = OrderCreationFingerprint.Compute(request);
        Order? existingByKey = await context.Orders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BusinessId == request.BusinessId && x.CreationIdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null)
            return existingByKey.CreationRequestHash == requestHash
                ? Result<OrderResponse>.Success(OrderMapper.Map(existingByKey))
                : Result<OrderResponse>.Failure(HandlerErrors.Conflict("Idempotency-Key farklı bir sipariş isteği için daha önce kullanıldı."));
        if (!string.IsNullOrWhiteSpace(request.ExternalId) && await context.Orders.AnyAsync(x => x.BusinessId == request.BusinessId && x.Source == request.Source && x.ExternalId == request.ExternalId.Trim(), cancellationToken))
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Bu kaynak sipariş numarası daha önce işlendi."));

        Order order = Order.Create(request.BusinessId, request.BranchId, request.ExternalId, request.CustomerName,
            request.CustomerPhone, request.DeliveryAddress, request.Source, request.TotalAmount, idempotencyKey,
            requestHash, requestContext.UserId, request.DeliveryLatitude, request.DeliveryLongitude,
            request.DeliveryInstructions, request.DeliveryLocationSource, request.DeliveryLocationAccuracy,
            request.DeliveryFulfillment);
        BusinessCreditAccount? creditAccount = await context.BusinessCreditAccounts
            .SingleOrDefaultAsync(x => x.BusinessId == request.BusinessId, cancellationToken);
        if (creditAccount is null)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("İşletmenin kredi hesabı bulunmuyor. Platform yöneticisi kredi yüklemelidir."));
        int creditBalance;
        try { creditBalance = creditAccount.Consume(1); }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        context.Orders.Add(order);
        context.CreditTransactions.Add(CreditTransaction.Create(request.BusinessId,
            CreditTransactionType.OrderConsumption, -1, creditBalance, order.Id,
            $"Sipariş #{order.Id.ToString("N")[..8]} için 1 kredi kullanıldı.", requestContext.UserId));
        BusinessDispatchSettings? settings = await context.BusinessDispatchSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == request.BusinessId, cancellationToken);
        if (settings?.AutoConfirmOrders == true)
        {
            order.ChangeStatus(OrderStatus.Confirmed, requestContext.UserId);
            if (order.DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier)
            {
                order.ChangeStatus(OrderStatus.WaitingForCourier, requestContext.UserId);
                OrderDispatchState dispatchState = OrderDispatchState.Create(order.Id, order.BusinessId);
                if (!settings.AutoAssignCouriers) dispatchState.MarkDisabled(DateTimeOffset.UtcNow);
                context.OrderDispatchStates.Add(dispatchState);
            }
        }
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Kredi bakiyesi başka bir sipariş tarafından kullanıldı. Tekrar deneyin."));
        }
        catch (DbUpdateException)
        {
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş veya kredi hareketi daha önce işlendi."));
        }
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }
}

public sealed class UpdateOrderHandler(ICoreDbContext context, IRequestContext requestContext, IOrderOperationsNotifier notifier)
    : IRequestHandler<UpdateOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(UpdateOrderCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.FindAsync([request.Id], cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        try { order.UpdateCustomer(request.CustomerName, request.CustomerPhone, request.DeliveryAddress,
            request.TotalAmount, request.DeliveryLatitude, request.DeliveryLongitude, request.DeliveryInstructions,
            request.DeliveryLocationSource, request.DeliveryLocationAccuracy); }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        await context.SaveChangesAsync(cancellationToken);
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }
}

public sealed class AssignOrderCourierHandler(ICoreDbContext context, IRequestContext requestContext, IOrderOperationsNotifier notifier)
    : IRequestHandler<AssignOrderCourierCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(AssignOrderCourierCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (order.DeliveryFulfillment != DeliveryFulfillmentType.MerchantCourier)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Bu sipariş sağlayıcı kuryesi veya gel-al modeliyle teslim edilecek."));
        Courier? courier = await context.Couriers.SingleOrDefaultAsync(x => x.Id == request.CourierId && x.IsActive, cancellationToken);
        if (courier is null || courier.BusinessId != order.BusinessId) return Result<OrderResponse>.Failure(HandlerErrors.Validation("Kurye bu işletmeye atanmış değil."));
        if (courier.BranchId.HasValue && courier.BranchId != order.BranchId)
            return Result<OrderResponse>.Failure(HandlerErrors.Validation("Kurye bu siparişin şubesine atanmış değil."));
        if (courier.Availability != CourierAvailability.Available ||
            !await context.CourierShifts.AnyAsync(x => x.CourierId == courier.Id && x.EndedAtUtc == null, cancellationToken))
            return Result<OrderResponse>.Failure(HandlerErrors.Validation("Kurye aktif mesaide ve müsait olmalıdır."));

        BusinessDispatchSettings? settings = await context.BusinessDispatchSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == order.BusinessId, cancellationToken);
        int maxActiveOrders = settings?.MaxActiveOrdersPerCourier ?? 2;
        OrderStatus[] activeStatuses = [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.OnTheWay, OrderStatus.DeliveryFailed];
        int activeOrderCount = await context.Orders.CountAsync(x => x.Id != order.Id && x.CourierId == courier.Id && activeStatuses.Contains(x.Status), cancellationToken);
        if (activeOrderCount >= maxActiveOrders)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Kurye aktif paket kapasitesine ulaştı."));
        courier.ReserveAssignmentSlot();

        Guid? previousCourierId = order.CourierId;
        if (previousCourierId == courier.Id) return Result<OrderResponse>.Success(OrderMapper.Map(order));
        try
        {
            if (previousCourierId.HasValue) order.ReassignCourier(courier.Id);
            else order.AssignCourier(courier.Id, requestContext.UserId);
        }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        if (!previousCourierId.HasValue) context.OrderStatusHistory.Add(order.StatusHistory.Single());
        else
        {
            Courier? previousCourier = await context.Couriers.FindAsync([previousCourierId.Value], cancellationToken);
            if (previousCourier is not null)
                await ChangeOrderStatusHandler.RefreshCourierDeliveryStatusAsync(context, previousCourier, order.Id, cancellationToken);
            context.NotificationOutbox.Add(NotificationOutboxMessage.CreateForOrder(order));
        }
        if (courier.DeliveryStatus == DeliveryStatus.WaitingForAssignment)
            courier.SetDeliveryStatus(DeliveryStatus.GoingToPickup);
        OrderDispatchState? dispatchState = await context.OrderDispatchStates
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);
        if (dispatchState is null)
        {
            dispatchState = OrderDispatchState.Create(order.Id, order.BusinessId);
            context.OrderDispatchStates.Add(dispatchState);
        }
        dispatchState.MarkAssigned(DateTimeOffset.UtcNow, courier.Id);
        context.DispatchAttempts.Add(DispatchAttempt.Create(order.Id, order.BusinessId, courier.Id,
            previousCourierId.HasValue ? "ManualReassignment" : "ManualAssignment", true, null));
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş başka bir işlem tarafından güncellendi.")); }
        OrderResponse response = OrderMapper.Map(order);
        if (previousCourierId.HasValue)
            await notifier.OrderReassignedAsync(response, previousCourierId.Value, cancellationToken);
        else
            await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }
}

public sealed class ClaimOrderHandler(ICoreDbContext context, IRequestContext requestContext,
    IOrderOperationsNotifier notifier, TimeProvider timeProvider)
    : IRequestHandler<ClaimOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(ClaimOrderCommand request, CancellationToken cancellationToken)
    {
        if (!requestContext.CourierId.HasValue)
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        Guid courierId = requestContext.CourierId.Value;
        Courier? courier = await context.Couriers.SingleOrDefaultAsync(x => x.Id == courierId && x.IsActive, cancellationToken);
        if (courier is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        if (!await context.CourierShifts.AnyAsync(x => x.CourierId == courierId && x.EndedAtUtc == null, cancellationToken))
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Paket üstlenmek için mesaiyi başlatın."));
        if (courier.Availability is CourierAvailability.Offline or CourierAvailability.OffShift or CourierAvailability.OnBreak)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Kurye şu anda paket üstlenemez."));
        BusinessDispatchSettings? settings = await context.BusinessDispatchSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == courier.BusinessId, cancellationToken);
        if (settings is not null && !settings.AllowCourierSelfClaim)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("İşletme, kuryelerin kendi paketlerini üstlenmesini kapattı."));
        OrderStatus[] activeStatuses = [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.OnTheWay, OrderStatus.DeliveryFailed];
        int activeOrderCount = await context.Orders.CountAsync(x => x.CourierId == courier.Id && activeStatuses.Contains(x.Status), cancellationToken);
        if (activeOrderCount >= (settings?.MaxActiveOrdersPerCourier ?? 2))
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Aktif paket kapasitenize ulaştınız."));
        courier.ReserveAssignmentSlot();

        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (order.BusinessId != courier.BusinessId) return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (courier.BranchId.HasValue && courier.BranchId != order.BranchId)
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (order.DeliveryFulfillment != DeliveryFulfillmentType.MerchantCourier)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Bu sipariş işletme kuryesi tarafından teslim edilmeyecek."));
        if (order.Status != OrderStatus.WaitingForCourier || order.CourierId.HasValue)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Bu paket artık üstlenmeye açık değil."));
        Result<double> proximity = await CourierOrderProximity.GetPickupDistanceAsync(context, courier.Id,
            order.BranchId, settings, timeProvider.GetUtcNow(), cancellationToken);
        if (proximity.IsFailure) return Result<OrderResponse>.Failure(proximity.Error);
        try { order.AssignCourier(courierId, requestContext.UserId); }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        context.OrderStatusHistory.Add(order.StatusHistory.Single());
        if (courier.DeliveryStatus == DeliveryStatus.WaitingForAssignment)
            courier.SetDeliveryStatus(DeliveryStatus.GoingToPickup);
        OrderDispatchState? dispatchState = await context.OrderDispatchStates
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);
        if (dispatchState is null)
        {
            dispatchState = OrderDispatchState.Create(order.Id, order.BusinessId);
            context.OrderDispatchStates.Add(dispatchState);
        }
        dispatchState.MarkAssigned(DateTimeOffset.UtcNow, courier.Id);
        context.DispatchAttempts.Add(DispatchAttempt.Create(order.Id, order.BusinessId, courier.Id,
            "SelfClaim", true, null));
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Paket başka bir kurye tarafından üstlenildi.")); }
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }
}

public sealed class GetAvailableOrdersHandler(ICoreDbContext context, IRequestContext requestContext,
    TimeProvider timeProvider)
    : IRequestHandler<GetAvailableOrdersQuery, Result<PagedResponse<AvailableOrderResponse>>>
{
    public async Task<Result<PagedResponse<AvailableOrderResponse>>> Handle(GetAvailableOrdersQuery request, CancellationToken cancellationToken)
    {
        if (!requestContext.CourierId.HasValue || !requestContext.BusinessId.HasValue)
            return Result<PagedResponse<AvailableOrderResponse>>.Failure(HandlerErrors.Forbidden);

        Guid courierId = requestContext.CourierId.Value;
        Guid businessId = requestContext.BusinessId.Value;
        Courier? courier = await context.Couriers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == courierId && x.BusinessId == businessId && x.IsActive, cancellationToken);
        if (courier is null)
            return Result<PagedResponse<AvailableOrderResponse>>.Failure(HandlerErrors.NotFound("Kurye"));
        bool isOnShift = await context.CourierShifts.AsNoTracking()
            .AnyAsync(x => x.CourierId == courierId && x.EndedAtUtc == null, cancellationToken);
        if (!isOnShift || courier.Availability is CourierAvailability.Offline or CourierAvailability.OffShift or CourierAvailability.OnBreak)
            return Result<PagedResponse<AvailableOrderResponse>>.Failure(
                HandlerErrors.Conflict("Yakındaki paketleri görmek için aktif mesaide ve müsait olmalısınız."));

        BusinessDispatchSettings? settings = await context.BusinessDispatchSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);
        CourierLocation? location = await CourierOrderProximity.GetFreshLocationAsync(context, courierId, settings,
            timeProvider.GetUtcNow(), cancellationToken);
        if (location is null)
            return Result<PagedResponse<AvailableOrderResponse>>.Failure(
                HandlerErrors.Conflict("Yakındaki paketleri görmek için güncel konumunuzu paylaşın."));

        IQueryable<Branch> branchQuery = context.Branches.AsNoTracking().Where(x =>
            x.BusinessId == businessId && x.IsActive && x.Latitude.HasValue && x.Longitude.HasValue);
        Guid? assignedBranchId = requestContext.BranchId ?? courier.BranchId;
        if (assignedBranchId.HasValue) branchQuery = branchQuery.Where(x => x.Id == assignedBranchId.Value);
        List<Branch> branches = await branchQuery.ToListAsync(cancellationToken);
        double radiusKm = CourierOrderProximity.ResolveRadiusKm(settings);
        Dictionary<Guid, (Branch Branch, double DistanceKm)> eligibleBranches = branches
            .Select(branch => (Branch: branch, DistanceKm: CourierAssignmentRanker.CalculateDistanceKm(
                location.Position.Y, location.Position.X, branch.Latitude, branch.Longitude)))
            .Where(item => item.DistanceKm.HasValue && item.DistanceKm.Value <= radiusKm)
            .ToDictionary(item => item.Branch.Id, item => (item.Branch, item.DistanceKm!.Value));

        if (eligibleBranches.Count == 0)
            return Result<PagedResponse<AvailableOrderResponse>>.Success(
                Pagination.FromItems(Array.Empty<AvailableOrderResponse>(), request.Page, request.PageSize));

        Guid[] branchIds = eligibleBranches.Keys.ToArray();
        List<Order> orders = await context.Orders.AsNoTracking().Where(x =>
                x.BusinessId == businessId && branchIds.Contains(x.BranchId) && x.CourierId == null &&
                x.Status == OrderStatus.WaitingForCourier &&
                x.DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier)
            .ToListAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        AvailableOrderResponse[] response = orders.Select(order =>
            {
                (Branch branch, double distanceKm) = eligibleBranches[order.BranchId];
                int waitingMinutes = Math.Max(0, (int)Math.Floor((now - order.CreatedAtUtc).TotalMinutes));
                return new AvailableOrderResponse(order.Id, order.BusinessId, order.BranchId, branch.Name,
                    branch.Address, Math.Round(distanceKm, 2), order.Source, order.Status, order.TotalAmount,
                    order.Currency, order.CreatedAtUtc, waitingMinutes);
            })
            .OrderBy(x => x.PickupDistanceKm)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToArray();
        return Result<PagedResponse<AvailableOrderResponse>>.Success(
            Pagination.FromItems(response, request.Page, request.PageSize));
    }
}

internal static class CourierOrderProximity
{
    private const double DefaultRadiusKm = 10;
    private const int DefaultLocationFreshnessMinutes = 5;

    public static double ResolveRadiusKm(BusinessDispatchSettings? settings) =>
        settings?.AssignmentRadiusKm ?? DefaultRadiusKm;

    public static async Task<CourierLocation?> GetFreshLocationAsync(ICoreDbContext context, Guid courierId,
        BusinessDispatchSettings? settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int freshnessMinutes = settings?.LocationFreshnessMinutes ?? DefaultLocationFreshnessMinutes;
        DateTimeOffset freshAfter = now.AddMinutes(-freshnessMinutes);
        return await context.CourierLocations.AsNoTracking()
            .Where(x => x.CourierId == courierId && x.RecordedAtUtc >= freshAfter)
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task<Result<double>> GetPickupDistanceAsync(ICoreDbContext context, Guid courierId,
        Guid branchId, BusinessDispatchSettings? settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        CourierLocation? location = await GetFreshLocationAsync(context, courierId, settings, now, cancellationToken);
        if (location is null)
            return Result<double>.Failure(HandlerErrors.Conflict(
                "Paket üstlenmek için güncel konumunuzu paylaşın."));
        Branch? branch = await context.Branches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == branchId && x.IsActive, cancellationToken);
        if (branch?.Latitude is null || branch.Longitude is null)
            return Result<double>.Failure(HandlerErrors.Conflict(
                "Şube konumu tanımlı olmadığı için bu paket üstlenilemiyor."));
        double? distanceKm = CourierAssignmentRanker.CalculateDistanceKm(location.Position.Y, location.Position.X,
            branch.Latitude, branch.Longitude);
        double radiusKm = ResolveRadiusKm(settings);
        if (!distanceKm.HasValue || distanceKm.Value > radiusKm)
            return Result<double>.Failure(HandlerErrors.Conflict(
                $"Paket {radiusKm:0.#} km olan üstlenme yarıçapınızın dışında."));
        return Result<double>.Success(distanceKm.Value);
    }
}

public sealed class ChangeOrderStatusHandler(ICoreDbContext context, IRequestContext requestContext, IOrderOperationsNotifier notifier)
    : IRequestHandler<ChangeOrderStatusCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(ChangeOrderStatusCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (requestContext.CourierId.HasValue && order.CourierId != requestContext.CourierId)
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (request.Status is OrderStatus.Cancelled or OrderStatus.DeliveryFailed)
            return Result<OrderResponse>.Failure(HandlerErrors.Validation("Bu durum için özel iptal/teslim edilemedi endpoint'ini kullanın."));
        if (request.Status == OrderStatus.Assigned)
            return Result<OrderResponse>.Failure(HandlerErrors.Validation("Kurye ataması için kurye atama endpoint'ini kullanın."));
        // A retried request (double tap, lost response on a weak mobile connection) must not surface as a failure.
        if (order.Status == request.Status) return Result<OrderResponse>.Success(OrderMapper.Map(order));
        try { order.ChangeStatus(request.Status, requestContext.UserId); }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        context.OrderStatusHistory.Add(order.StatusHistory.Single());

        if (request.Status == OrderStatus.WaitingForCourier && order.DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier)
        {
            BusinessDispatchSettings? settings = await context.BusinessDispatchSettings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == order.BusinessId, cancellationToken);
            OrderDispatchState dispatchState = OrderDispatchState.Create(order.Id, order.BusinessId);
            if (settings?.AutoAssignCouriers != true) dispatchState.MarkDisabled(DateTimeOffset.UtcNow);
            context.OrderDispatchStates.Add(dispatchState);
        }

        if (order.CourierId.HasValue)
        {
            Courier? courier = await context.Couriers.FindAsync([order.CourierId.Value], cancellationToken);
            if (courier is not null)
            {
                if (request.Status == OrderStatus.PickedUp || request.Status == OrderStatus.OnTheWay) courier.SetDeliveryStatus(DeliveryStatus.Delivering);
                if (request.Status is OrderStatus.Delivered or OrderStatus.Returned or OrderStatus.Cancelled)
                    await RefreshCourierDeliveryStatusAsync(context, courier, order.Id, cancellationToken);
            }
        }

        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş başka bir işlem tarafından güncellendi.")); }
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }

    internal static async Task RefreshCourierDeliveryStatusAsync(ICoreDbContext context, Courier courier,
        Guid completedOrderId, CancellationToken cancellationToken)
    {
        OrderStatus[] activeStatuses = [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.OnTheWay, OrderStatus.DeliveryFailed];
        List<OrderStatus> remainingStatuses = await context.Orders.AsNoTracking()
            .Where(x => x.Id != completedOrderId && x.CourierId == courier.Id && activeStatuses.Contains(x.Status))
            .Select(x => x.Status).ToListAsync(cancellationToken);
        courier.SetDeliveryStatus(remainingStatuses.Any(x => x is OrderStatus.PickedUp or OrderStatus.OnTheWay or OrderStatus.DeliveryFailed)
            ? DeliveryStatus.Delivering
            : remainingStatuses.Count > 0 ? DeliveryStatus.GoingToPickup : DeliveryStatus.WaitingForAssignment);
    }
}

public sealed class CancelOrderHandler(ICoreDbContext context, IRequestContext requestContext, IOrderOperationsNotifier notifier)
    : IRequestHandler<CancelOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        BusinessCreditAccount? creditAccount = await context.BusinessCreditAccounts
            .SingleOrDefaultAsync(x => x.BusinessId == order.BusinessId, cancellationToken);
        if (creditAccount is null)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("İşletmenin kredi hesabı bulunamadı."));
        bool alreadyRefunded = await context.CreditTransactions.AnyAsync(x => x.BusinessId == order.BusinessId &&
            x.OrderId == order.Id && x.Type == CreditTransactionType.Refund, cancellationToken);
        if (alreadyRefunded)
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Bu siparişin kredisi daha önce iade edildi."));
        try { order.Cancel(request.Reason, requestContext.UserId); }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        context.OrderStatusHistory.Add(order.StatusHistory.Single());
        int balanceAfter = creditAccount.Refund(1);
        context.CreditTransactions.Add(CreditTransaction.Create(order.BusinessId, CreditTransactionType.Refund,
            1, balanceAfter, order.Id, $"İptal edilen sipariş #{order.Id.ToString("N")[..8]} için 1 kredi iade edildi.",
            requestContext.UserId, $"order-cancel-{order.Id:N}"));
        await ReleaseCourierAsync(context, order, cancellationToken);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş başka bir işlem tarafından güncellendi.")); }
        catch (DbUpdateException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş iptali veya kredi iadesi daha önce işlendi.")); }
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }

    internal static async Task ReleaseCourierAsync(ICoreDbContext context, Order order, CancellationToken cancellationToken)
    {
        if (!order.CourierId.HasValue) return;
        Courier? courier = await context.Couriers.FindAsync([order.CourierId.Value], cancellationToken);
        if (courier is not null)
            await ChangeOrderStatusHandler.RefreshCourierDeliveryStatusAsync(context, courier, order.Id, cancellationToken);
    }
}

public sealed class ReportDeliveryFailureHandler(ICoreDbContext context, IRequestContext requestContext, IOrderOperationsNotifier notifier)
    : IRequestHandler<ReportDeliveryFailureCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(ReportDeliveryFailureCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId) ||
            (requestContext.CourierId.HasValue && order.CourierId != requestContext.CourierId))
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (order.Status == OrderStatus.DeliveryFailed) return Result<OrderResponse>.Success(OrderMapper.Map(order));
        try { order.ReportDeliveryFailure(request.Reason, requestContext.UserId); }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        context.OrderStatusHistory.Add(order.StatusHistory.Single());
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş başka bir işlem tarafından güncellendi.")); }
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }
}

public static class OrderMapper
{
    public static OrderResponse Map(Order x) => new(x.Id, x.BusinessId, x.BranchId, x.CourierId, x.ExternalId,
        x.CustomerName, x.CustomerPhone, x.DeliveryAddress, x.Source, x.Status, x.TotalAmount, x.Currency, x.CreatedAtUtc,
        x.CancellationReason, x.DeliveryFailureReason, x.AllowedNextStatuses, DeliveryLatitude: x.DeliveryLatitude,
        DeliveryLongitude: x.DeliveryLongitude, DeliveryInstructions: x.DeliveryInstructions,
        DeliveryLocationSource: x.DeliveryLocationSource, DeliveryLocationAccuracy: x.DeliveryLocationAccuracy,
        DeliveryFulfillment: x.DeliveryFulfillment);
}

internal static class OrderCreationFingerprint
{
    public static string Compute(CreateOrderCommand request)
    {
        string canonical = string.Join('\u001f',
            request.BusinessId.ToString("D"),
            request.BranchId.ToString("D"),
            request.ExternalId.Trim(),
            request.CustomerName.Trim(),
            request.CustomerPhone.Trim(),
            request.DeliveryAddress.Trim(),
            request.DeliveryLatitude?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            request.DeliveryLongitude?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            request.DeliveryInstructions?.Trim() ?? string.Empty,
            ((int)request.DeliveryLocationSource).ToString(CultureInfo.InvariantCulture),
            ((int)request.DeliveryLocationAccuracy).ToString(CultureInfo.InvariantCulture),
            ((int)request.DeliveryFulfillment).ToString(CultureInfo.InvariantCulture),
            ((int)request.Source).ToString(CultureInfo.InvariantCulture),
            request.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
