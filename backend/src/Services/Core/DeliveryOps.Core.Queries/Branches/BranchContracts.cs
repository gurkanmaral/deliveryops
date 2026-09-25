using DeliveryOps.BuildingBlocks.Application;
using MediatR;

namespace DeliveryOps.Core.Queries.Branches;

public sealed record BranchResponse(
    Guid Id,
    Guid BusinessId,
    string Name,
    string Address,
    double? Latitude,
    double? Longitude,
    bool IsActive);

public sealed record GetBranchesQuery(Guid? BusinessId, int Page = 1, int PageSize = 20, string? Search = null, string Sort = "name")
    : IRequest<Result<PagedResponse<BranchResponse>>>;
public sealed record GetBranchQuery(Guid Id) : IRequest<Result<BranchResponse>>;
public sealed record CreateBranchCommand(Guid BusinessId, string Name, string Address, double? Latitude, double? Longitude) : IRequest<Result<BranchResponse>>;
public sealed record UpdateBranchCommand(Guid Id, string Name, string Address, double? Latitude, double? Longitude) : IRequest<Result<BranchResponse>>;
public sealed record DeactivateBranchCommand(Guid Id) : IRequest<Result>;
