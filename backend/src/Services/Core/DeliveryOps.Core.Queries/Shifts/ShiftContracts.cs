using DeliveryOps.BuildingBlocks.Application;
using MediatR;

namespace DeliveryOps.Core.Queries.Shifts;

public sealed record CourierShiftResponse(Guid Id, Guid CourierId, Guid BusinessId,
    DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc);
public sealed record StartCourierShiftCommand(Guid CourierId) : IRequest<Result<CourierShiftResponse>>;
public sealed record EndCourierShiftCommand(Guid CourierId) : IRequest<Result<CourierShiftResponse>>;
public sealed record GetActiveCourierShiftsQuery(Guid? BusinessId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<CourierShiftResponse>>>;
