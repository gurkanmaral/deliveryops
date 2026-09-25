using DeliveryOps.Auth.Api.Domain;
using DeliveryOps.Auth.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace DeliveryOps.Auth.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
    TokenService tokenService, IWebHostEnvironment environment, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        ApplicationUser? user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive) return Unauthorized();
        IList<string> roles = await userManager.GetRolesAsync(user);
        return Ok(new CurrentUserResponse(user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName,
            user.BusinessId, user.BranchId, user.CourierId, roles.ToArray(), RolePermissions.Resolve(roles).Order().ToArray()));
    }

    [HttpPost("login")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive) return InvalidCredentials();
        Microsoft.AspNetCore.Identity.SignInResult signIn =
            await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signIn.Succeeded)
            return InvalidCredentials();

        TokenResponse tokens = await tokenService.IssueAsync(user, cancellationToken);
        SetRefreshCookie(tokens.RefreshToken);
        return Ok(new AuthResponse(tokens.AccessToken, tokens.ExpiresAtUtc));
    }

    [HttpPost("mobile/login")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<MobileAuthResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MobileAuthResponse>> MobileLogin(LoginRequest request, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive) return InvalidCredentials();
        Microsoft.AspNetCore.Identity.SignInResult signIn =
            await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signIn.Succeeded)
            return InvalidCredentials();
        if (!user.CourierId.HasValue)
            return Forbid();
        TokenResponse tokens = await tokenService.IssueAsync(user, cancellationToken);
        return Ok(new MobileAuthResponse(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("token-refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue("deliveryops_refresh", out string? rawToken)) return Unauthorized();
        TokenResponse? response = await tokenService.RefreshAsync(rawToken, cancellationToken);
        if (response is null) return Unauthorized();
        SetRefreshCookie(response.RefreshToken);
        return Ok(new AuthResponse(response.AccessToken, response.ExpiresAtUtc));
    }

    [HttpPost("mobile/refresh")]
    [EnableRateLimiting("token-refresh")]
    public async Task<ActionResult<MobileAuthResponse>> MobileRefresh(MobileRefreshRequest request, CancellationToken cancellationToken)
    {
        TokenResponse? response = await tokenService.RefreshAsync(request.RefreshToken, cancellationToken);
        return response is null
            ? Unauthorized()
            : Ok(new MobileAuthResponse(response.AccessToken, response.RefreshToken, response.ExpiresAtUtc));
    }

    [HttpPost("mobile/logout")]
    public async Task<ActionResult> MobileLogout(MobileRefreshRequest request, CancellationToken cancellationToken)
    {
        await tokenService.RevokeAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue("deliveryops_refresh", out string? rawToken))
        {
            await tokenService.RevokeAsync(rawToken, cancellationToken);
        }
        Response.Cookies.Delete("deliveryops_refresh", new CookieOptions { Path = "/api/v1/auth" });
        return NoContent();
    }

    private void SetRefreshCookie(string refreshToken) => Response.Cookies.Append("deliveryops_refresh", refreshToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment() || Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Expires = timeProvider.GetUtcNow().AddDays(14),
        Path = "/api/v1/auth"
    });

    private UnauthorizedObjectResult InvalidCredentials() => Unauthorized(new ProblemDetails
    {
        Title = "Giriş başarısız",
        Detail = "E-posta veya şifre hatalı.",
        Status = StatusCodes.Status401Unauthorized
    });
}

public sealed record LoginRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 1)] string Password);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
public sealed record MobileAuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);
public sealed record MobileRefreshRequest(
    [Required, StringLength(512, MinimumLength = 32)] string RefreshToken);
public sealed record CurrentUserResponse(Guid Id, string Email, string FirstName, string LastName,
    Guid? BusinessId, Guid? BranchId, Guid? CourierId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
