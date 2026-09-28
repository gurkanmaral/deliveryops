using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Dispatch;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Dispatch;

public sealed class GetDispatchQueueHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetDispatchQueueQuery, Result<PagedResponse<DispatchQueueItemResponse>>>
{
    public async Task<Result<PagedResponse<DispatchQueueItemResponse>>> Handle(GetDispatchQueueQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!businessId.HasValue) return Result<PagedResponse<DispatchQueueItemResponse>>.Failure(HandlerErrors.Validation("İşletme seçilmelidir."));
        if (!TenantAccess.CanAccess(requestContext, businessId.Value)) return Result<PagedResponse<DispatchQueueItemResponse>>.Failure(HandlerErrors.Forbidden);

        IQueryable<DispatchQueueItemResponse> query = from order in context.Orders.AsNoTracking()
            join state in context.OrderDispatchStates.AsNoTracking() on order.Id equals state.OrderId into states
            from state in states.DefaultIfEmpty()
            where order.BusinessId == businessId && order.Status == OrderStatus.WaitingForCourier && order.CourierId == null
                && order.DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier
                && (!requestContext.BranchId.HasValue || order.BranchId == requestContext.BranchId.Value)
            orderby order.CreatedAtUtc
            select new DispatchQueueItemResponse(order.Id, order.BusinessId, order.BranchId, order.CustomerName,
                order.DeliveryAddress, order.CreatedAtUtc, state == null ? DispatchStatus.Pending : state.Status,
                state == null ? 0 : state.AttemptCount, state == null ? null : state.LastReason,
                state == null ? null : state.NextAttemptAtUtc);
        PagedResponse<DispatchQueueItemResponse> items = await query.ToPagedAsync(
            request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<DispatchQueueItemResponse>>.Success(items);
    }
}

public sealed class GetCourierSuggestionsHandler(ICoreDbContext context, IRequestContext requestContext,
    DispatchCandidateFinder candidateFinder)
    : IRequestHandler<GetCourierSuggestionsQuery, Result<PagedResponse<CourierSuggestionResponse>>>
{
    public async Task<Result<PagedResponse<CourierSuggestionResponse>>> Handle(GetCourierSuggestionsQuery request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);
        if (order is null) return Result<PagedResponse<CourierSuggestionResponse>>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result<PagedResponse<CourierSuggestionResponse>>.Failure(HandlerErrors.Forbidden);
        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.WaitingForCourier or OrderStatus.Assigned))
            return Result<PagedResponse<CourierSuggestionResponse>>.Failure(HandlerErrors.Conflict("Bu sipariş için kurye önerisi üretilemez."));

        BusinessDispatchSettings settings = await context.BusinessDispatchSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == order.BusinessId, cancellationToken)
            ?? BusinessDispatchSettings.CreateDefault(order.BusinessId);
        IReadOnlyList<CourierSuggestionResponse> candidates = await candidateFinder.FindAsync(order, settings, cancellationToken);
        return Result<PagedResponse<CourierSuggestionResponse>>.Success(
            Pagination.FromItems(candidates, request.Page, request.PageSize));
    }
}

public sealed class GetDispatchAttemptsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetDispatchAttemptsQuery, Result<PagedResponse<DispatchAttemptResponse>>>
{
    public async Task<Result<PagedResponse<DispatchAttemptResponse>>> Handle(GetDispatchAttemptsQuery request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);
        if (order is null) return Result<PagedResponse<DispatchAttemptResponse>>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result<PagedResponse<DispatchAttemptResponse>>.Failure(HandlerErrors.Forbidden);

        IQueryable<DispatchAttemptResponse> query = from attempt in context.DispatchAttempts.AsNoTracking()
            join courier in context.Couriers.AsNoTracking() on attempt.CourierId equals courier.Id into couriers
            from courier in couriers.DefaultIfEmpty()
            where attempt.OrderId == order.Id
            orderby attempt.CreatedAtUtc descending
            select new DispatchAttemptResponse(attempt.Id, attempt.OrderId, attempt.CourierId,
                courier == null ? null : courier.FirstName + " " + courier.LastName,
                attempt.Trigger, attempt.WasSuccessful, attempt.Reason, attempt.CreatedAtUtc);
        PagedResponse<DispatchAttemptResponse> attempts = await query.ToPagedAsync(
            request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<DispatchAttemptResponse>>.Success(attempts);
    }
}

public sealed class RetryDispatchHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<RetryDispatchCommand, Result>
{
    public async Task<Result> Handle(RetryDispatchCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.OrderId, cancellationToken);
        if (order is null) return Result.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId)) return Result.Failure(HandlerErrors.Forbidden);
        if (order.Status != OrderStatus.WaitingForCourier || order.CourierId.HasValue ||
            order.DeliveryFulfillment != DeliveryFulfillmentType.MerchantCourier)
            return Result.Failure(HandlerErrors.Conflict("Yalnızca kurye bekleyen sipariş yeniden denenebilir."));
        if (!await context.BusinessDispatchSettings.AnyAsync(x => x.BusinessId == order.BusinessId && x.AutoAssignCouriers, cancellationToken))
            return Result.Failure(HandlerErrors.Conflict("Önce işletme için otomatik kurye atamayı açın."));

        OrderDispatchState? state = await context.OrderDispatchStates.SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);
        if (state is null)
        {
            state = OrderDispatchState.Create(order.Id, order.BusinessId);
            context.OrderDispatchStates.Add(state);
        }
        state.Queue(DateTimeOffset.UtcNow);
        context.DispatchAttempts.Add(DispatchAttempt.Create(order.Id, order.BusinessId, null,
            "ManualRetry", true, "Operatör tarafından yeniden deneme kuyruğuna alındı."));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
