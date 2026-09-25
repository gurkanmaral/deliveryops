using System.Security.Claims;
using DeliveryOps.Auth.Api.Domain;
using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace DeliveryOps.Auth.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.UsersRead)]
    public async Task<ActionResult<PagedResponse<UserResponse>>> GetAll(
        [FromQuery] Guid? businessId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Guid? tenantId = CurrentBusinessId();
        IQueryable<ApplicationUser> query = userManager.Users.AsNoTracking();
        if (!User.IsInRole(AppRoles.PlatformAdmin))
        {
            if (tenantId is null) return Forbid();
            query = query.Where(user => user.BusinessId == tenantId);
        }
        else if (businessId.HasValue)
        {
            query = query.Where(user => user.BusinessId == businessId);
        }

        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<ApplicationUser> users = await query.OrderBy(user => user.Email)
            .ThenBy(user => user.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        List<UserResponse> response = [];
        foreach (ApplicationUser user in users)
            response.Add(ToResponse(user, (await userManager.GetRolesAsync(user)).ToArray()));

        return Ok(new PagedResponse<UserResponse>(response, page, pageSize, totalCount));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.UsersWrite)]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request)
    {
        ActionResult? validation = ValidateAssignment(request);
        if (validation is not null) return validation;

        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            BusinessId = request.Role == AppRoles.PlatformAdmin ? null : EffectiveBusinessId(request.BusinessId),
            BranchId = request.BranchId,
            CourierId = request.CourierId,
            EmailConfirmed = true
        };
        IdentityResult created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded) return IdentityErrors(created);

        IdentityResult roleAdded = await userManager.AddToRoleAsync(user, request.Role);
        if (!roleAdded.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return IdentityErrors(roleAdded);
        }

        return Created($"/api/v1/users/{user.Id}", ToResponse(user, [request.Role]));
    }

    [HttpPatch("{id:guid}/active")]
    [Authorize(Policy = Permissions.UsersWrite)]
    public async Task<ActionResult<UserResponse>> SetActive(Guid id, SetUserActiveRequest request)
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid currentUserId) && currentUserId == id)
            return BadRequest(new ProblemDetails { Title = "Kendi hesabınızın durumunu değiştiremezsiniz." });

        ApplicationUser? target = await userManager.FindByIdAsync(id.ToString());
        if (target is null) return NotFound();
        string[] targetRoles = (await userManager.GetRolesAsync(target)).ToArray();
        if (!CanManage(target, targetRoles)) return Forbid();

        target.IsActive = request.IsActive;
        IdentityResult updated = await userManager.UpdateAsync(target);
        return updated.Succeeded ? Ok(ToResponse(target, targetRoles)) : IdentityErrors(updated);
    }

    private ActionResult? ValidateAssignment(CreateUserRequest request)
    {
        string role = request.Role;
        if (!AppRoles.All.Contains(role, StringComparer.Ordinal))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["role"] = ["Geçersiz rol."] }));

        bool isPlatformAdmin = User.IsInRole(AppRoles.PlatformAdmin);
        if (!isPlatformAdmin && role != AppRoles.BusinessStaff) return Forbid();

        Guid? effectiveBusinessId = EffectiveBusinessId(request.BusinessId);
        if (role != AppRoles.PlatformAdmin && effectiveBusinessId is null)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["businessId"] = ["İşletme kullanıcısı için işletme zorunludur."] }));

        if (role == AppRoles.PlatformAdmin && (request.BusinessId.HasValue || request.BranchId.HasValue || request.CourierId.HasValue))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["businessId"] = ["Platform yöneticisine tenant veya kurye atanamaz."] }));
        if (role == AppRoles.Courier && !request.CourierId.HasValue)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["courierId"] = ["Kurye rolü için kurye zorunludur."] }));
        if (role != AppRoles.Courier && request.CourierId.HasValue)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["courierId"] = ["Kurye kimliği yalnızca kurye rolüne atanabilir."] }));
        if (request.BranchId.HasValue && effectiveBusinessId is null)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["branchId"] = ["Şube ataması için işletme zorunludur."] }));

        if (!isPlatformAdmin && effectiveBusinessId != CurrentBusinessId()) return Forbid();
        return null;
    }

    private bool CanManage(ApplicationUser target, IReadOnlyCollection<string> targetRoles)
    {
        if (User.IsInRole(AppRoles.PlatformAdmin)) return true;
        return target.BusinessId == CurrentBusinessId() && targetRoles.Count == 1 && targetRoles.Contains(AppRoles.BusinessStaff);
    }

    private Guid? EffectiveBusinessId(Guid? requestedBusinessId) =>
        User.IsInRole(AppRoles.PlatformAdmin) ? requestedBusinessId : CurrentBusinessId();

    private Guid? CurrentBusinessId() =>
        Guid.TryParse(User.FindFirstValue("business_id"), out Guid businessId) ? businessId : null;

    private ActionResult IdentityErrors(IdentityResult result) => BadRequest(new ValidationProblemDetails(result.Errors
        .GroupBy(error => error.Code)
        .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray())));

    private static UserResponse ToResponse(ApplicationUser user, IReadOnlyList<string> roles) => new(
        user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName, user.BusinessId,
        user.BranchId, user.CourierId, user.IsActive, roles);
}

public sealed record CreateUserRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 10)] string Password,
    [Required, StringLength(100, MinimumLength = 1)] string FirstName,
    [Required, StringLength(100, MinimumLength = 1)] string LastName,
    [Required, StringLength(50)] string Role,
    Guid? BusinessId,
    Guid? BranchId,
    Guid? CourierId);

public sealed record SetUserActiveRequest(bool IsActive);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    Guid? BusinessId,
    Guid? BranchId,
    Guid? CourierId,
    bool IsActive,
    IReadOnlyList<string> Roles);
