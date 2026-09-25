using System.Security.Claims;
using DeliveryOps.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Security;

public sealed class CourierIdentityValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, CoreDbContext context)
    {
        ClaimsPrincipal user = httpContext.User;
        string? courierClaim = user.FindFirstValue("courier_id");
        if (user.Identity?.IsAuthenticated == true && courierClaim is not null)
        {
            bool courierIdIsValid = Guid.TryParse(courierClaim, out Guid courierId);
            bool businessIdIsValid = Guid.TryParse(user.FindFirstValue("business_id"), out Guid businessId);
            bool claimsAreValid = courierIdIsValid && businessIdIsValid;
            Guid? branchId = Guid.TryParse(user.FindFirstValue("branch_id"), out Guid parsedBranchId)
                ? parsedBranchId
                : null;
            bool identityMatches = claimsAreValid && await context.Couriers.AsNoTracking().AnyAsync(courier =>
                courier.Id == courierId && courier.BusinessId == businessId && courier.BranchId == branchId && courier.IsActive,
                httpContext.RequestAborted);
            if (!identityMatches)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Kurye kimliği geçersiz",
                    Detail = "Kurye hesabının işletme veya şube ataması güncel değil. Yeniden giriş yapın."
                }, httpContext.RequestAborted);
                return;
            }
        }

        await next(httpContext);
    }
}
