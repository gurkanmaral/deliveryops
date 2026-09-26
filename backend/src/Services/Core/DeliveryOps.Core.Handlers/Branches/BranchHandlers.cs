using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Branches;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Branches;

public sealed class GetBranchesHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetBranchesQuery, Result<PagedResponse<BranchResponse>>>
{
    public async Task<Result<PagedResponse<BranchResponse>>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && businessId is null) return Result<PagedResponse<BranchResponse>>.Failure(HandlerErrors.Forbidden);

        IQueryable<Branch> query = context.Branches.AsNoTracking();
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        if (requestContext.BranchId.HasValue) query = query.Where(x => x.Id == requestContext.BranchId.Value);
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Name.Contains(request.Search) || x.Address.Contains(request.Search));
        query = request.Sort.ToLowerInvariant() switch { "-name" => query.OrderByDescending(x => x.Name), "-created" => query.OrderByDescending(x => x.CreatedAtUtc), _ => query.OrderBy(x => x.Name) };
        PagedResponse<BranchResponse> response = await query.Select(x => new BranchResponse(x.Id, x.BusinessId, x.Name, x.Address, x.Latitude, x.Longitude, x.IsActive))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<BranchResponse>>.Success(response);
    }
}

public sealed class GetBranchHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetBranchQuery, Result<BranchResponse>>
{
    public async Task<Result<BranchResponse>> Handle(GetBranchQuery request, CancellationToken cancellationToken)
    {
        Branch? branch = await context.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (branch is null) return Result<BranchResponse>.Failure(HandlerErrors.NotFound("Şube"));
        if (!TenantAccess.CanAccessBranch(requestContext, branch.BusinessId, branch.Id)) return Result<BranchResponse>.Failure(HandlerErrors.Forbidden);
        return Result<BranchResponse>.Success(BranchMapper.Map(branch));
    }
}

public sealed class CreateBranchHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<CreateBranchCommand, Result<BranchResponse>>
{
    public async Task<Result<BranchResponse>> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        if (!TenantAccess.CanAccess(requestContext, request.BusinessId)) return Result<BranchResponse>.Failure(HandlerErrors.Forbidden);
        if (requestContext.BranchId.HasValue) return Result<BranchResponse>.Failure(HandlerErrors.Forbidden);
        if (!await context.Businesses.AnyAsync(x => x.Id == request.BusinessId && x.IsActive, cancellationToken))
            return Result<BranchResponse>.Failure(HandlerErrors.NotFound("İşletme"));

        Branch branch = Branch.Create(request.BusinessId, request.Name, request.Address, request.Latitude, request.Longitude);
        context.Branches.Add(branch);
        await context.SaveChangesAsync(cancellationToken);
        return Result<BranchResponse>.Success(BranchMapper.Map(branch));
    }
}

public sealed class UpdateBranchHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<UpdateBranchCommand, Result<BranchResponse>>
{
    public async Task<Result<BranchResponse>> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        Branch? branch = await context.Branches.FindAsync([request.Id], cancellationToken);
        if (branch is null) return Result<BranchResponse>.Failure(HandlerErrors.NotFound("Şube"));
        if (!TenantAccess.CanAccessBranch(requestContext, branch.BusinessId, branch.Id)) return Result<BranchResponse>.Failure(HandlerErrors.Forbidden);
        branch.Update(request.Name, request.Address, request.Latitude, request.Longitude);
        await context.SaveChangesAsync(cancellationToken);
        return Result<BranchResponse>.Success(BranchMapper.Map(branch));
    }
}

public sealed class DeactivateBranchHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<DeactivateBranchCommand, Result>
{
    public async Task<Result> Handle(DeactivateBranchCommand request, CancellationToken cancellationToken)
    {
        Branch? branch = await context.Branches.FindAsync([request.Id], cancellationToken);
        if (branch is null) return Result.Failure(HandlerErrors.NotFound("Şube"));
        if (!TenantAccess.CanAccessBranch(requestContext, branch.BusinessId, branch.Id)) return Result.Failure(HandlerErrors.Forbidden);
        branch.Deactivate();
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class BranchMapper
{
    public static BranchResponse Map(Branch x) => new(x.Id, x.BusinessId, x.Name, x.Address, x.Latitude, x.Longitude, x.IsActive);
}
