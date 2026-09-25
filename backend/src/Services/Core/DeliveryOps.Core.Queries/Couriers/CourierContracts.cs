using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Enums;
using MediatR;

namespace DeliveryOps.Core.Queries.Couriers;

public sealed record CourierResponse(
    Guid Id,
    Guid BusinessId,
    Guid? BranchId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    CourierAvailability Availability,
    DeliveryStatus DeliveryStatus,
    bool IsActive);

public sealed record GetCouriersQuery(Guid? BusinessId, Guid? BranchId, bool IncludeInactive = false,
    int Page = 1, int PageSize = 20, string? Search = null, string Sort = "name")
    : IRequest<Result<PagedResponse<CourierResponse>>>;
public sealed record GetCourierQuery(Guid Id) : IRequest<Result<CourierResponse>>;
public sealed record GetCurrentCourierQuery : IRequest<Result<CurrentCourierResponse>>;
public sealed record CurrentCourierResponse(Guid Id, Guid BusinessId, Guid? BranchId, string FirstName,
    string LastName, string PhoneNumber, CourierAvailability Availability, DeliveryStatus DeliveryStatus,
    bool IsActive, bool IsShiftActive, DateTimeOffset? ShiftStartedAtUtc);
public sealed record CreateCourierCommand(Guid BusinessId, Guid? BranchId, string FirstName, string LastName, string PhoneNumber)
    : IRequest<Result<CourierResponse>>;
public sealed record UpdateCourierCommand(Guid Id, string FirstName, string LastName, string PhoneNumber)
    : IRequest<Result<CourierResponse>>;
public sealed record AssignCourierToBusinessCommand(Guid Id, Guid BusinessId, Guid? BranchId)
    : IRequest<Result<CourierResponse>>;
public sealed record UpdateCourierStatusCommand(Guid Id, CourierAvailability Availability, DeliveryStatus DeliveryStatus)
    : IRequest<Result<CourierResponse>>;
public sealed record DeactivateCourierCommand(Guid Id) : IRequest<Result>;
