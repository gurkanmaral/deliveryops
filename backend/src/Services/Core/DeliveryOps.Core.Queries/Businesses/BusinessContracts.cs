using DeliveryOps.BuildingBlocks.Application;
using MediatR;

namespace DeliveryOps.Core.Queries.Businesses;

public sealed record BusinessSummary(Guid Id, string Name, string Code, bool IsActive, int BranchCount);
public sealed record GetBusinessesQuery(int Page = 1, int PageSize = 20, string? Search = null, string Sort = "name")
    : IRequest<PagedResponse<BusinessSummary>>;
public sealed record CreateBusinessCommand(string Name, string Code) : IRequest<Result<BusinessSummary>>;
public sealed record UpdateBusinessCommand(Guid Id, string Name) : IRequest<Result<BusinessSummary>>;
public sealed record SetBusinessActiveCommand(Guid Id, bool IsActive) : IRequest<Result<BusinessSummary>>;
