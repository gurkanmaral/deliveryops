using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Businesses;
using MediatR;
using Microsoft.EntityFrameworkCore;
using DeliveryOps.Core.Handlers.Common;

namespace DeliveryOps.Core.Handlers.Businesses;

public sealed class GetBusinessesHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetBusinessesQuery, PagedResponse<BusinessSummary>>
{
    public async Task<PagedResponse<BusinessSummary>> Handle(
        GetBusinessesQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<Business> query = context.Businesses
            .AsNoTracking()
            .Where(business => requestContext.IsPlatformAdmin || business.Id == requestContext.BusinessId);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(x => x.Name.Contains(request.Search) || x.Code.Contains(request.Search));
        query = request.Sort.ToLowerInvariant() switch
        {
            "-name" => query.OrderByDescending(x => x.Name),
            "code" => query.OrderBy(x => x.Code),
            "-created" => query.OrderByDescending(x => x.CreatedAtUtc),
            _ => query.OrderBy(x => x.Name)
        };
        return await query
            .Select(business => new BusinessSummary(
                business.Id,
                business.Name,
                business.Code,
                business.IsActive,
                business.Branches.Count))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
    }
}

public sealed class CreateBusinessHandler(ICoreDbContext context)
    : IRequestHandler<CreateBusinessCommand, Result<BusinessSummary>>
{
    public async Task<Result<BusinessSummary>> Handle(
        CreateBusinessCommand request,
        CancellationToken cancellationToken)
    {
        string normalizedCode = request.Code.Trim().ToUpperInvariant();
        bool codeExists = await context.Businesses
            .AnyAsync(business => business.Code == normalizedCode, cancellationToken);

        if (codeExists)
        {
            return Result<BusinessSummary>.Failure(
                new Error("business.code_conflict", "Bu işletme kodu zaten kullanılıyor."));
        }

        Business business = Business.Create(request.Name, normalizedCode);
        context.Businesses.Add(business);
        context.BusinessCreditAccounts.Add(BusinessCreditAccount.Create(business.Id));
        await context.SaveChangesAsync(cancellationToken);

        return Result<BusinessSummary>.Success(
            new BusinessSummary(business.Id, business.Name, business.Code, business.IsActive, 0));
    }
}

public sealed class UpdateBusinessHandler(ICoreDbContext context)
    : IRequestHandler<UpdateBusinessCommand, Result<BusinessSummary>>
{
    public async Task<Result<BusinessSummary>> Handle(UpdateBusinessCommand request, CancellationToken cancellationToken)
    {
        Business? business = await context.Businesses.Include(x => x.Branches).SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (business is null) return Result<BusinessSummary>.Failure(new Error("not_found", "İşletme bulunamadı."));
        business.Update(request.Name);
        await context.SaveChangesAsync(cancellationToken);
        return Result<BusinessSummary>.Success(new BusinessSummary(business.Id, business.Name, business.Code, business.IsActive, business.Branches.Count));
    }
}

public sealed class SetBusinessActiveHandler(ICoreDbContext context)
    : IRequestHandler<SetBusinessActiveCommand, Result<BusinessSummary>>
{
    public async Task<Result<BusinessSummary>> Handle(SetBusinessActiveCommand request, CancellationToken cancellationToken)
    {
        Business? business = await context.Businesses.Include(x => x.Branches).SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (business is null) return Result<BusinessSummary>.Failure(new Error("not_found", "İşletme bulunamadı."));
        business.SetActive(request.IsActive);
        await context.SaveChangesAsync(cancellationToken);
        return Result<BusinessSummary>.Success(new BusinessSummary(business.Id, business.Name, business.Code, business.IsActive, business.Branches.Count));
    }
}
