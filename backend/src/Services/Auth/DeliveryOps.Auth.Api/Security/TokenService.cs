using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DeliveryOps.Auth.Api.Domain;
using DeliveryOps.Auth.Api.Persistence;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DeliveryOps.Auth.Api.Security;

public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);

public sealed class TokenService(
    UserManager<ApplicationUser> userManager,
    AuthDbContext context,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider,
    IWebHostEnvironment environment)
{
    private readonly JwtOptions _options = options.Value;
    private readonly SecurityKey _signingKey = JwtKeyMaterial.CreateSigningKey(
        options.Value.PrivateKeyPem, options.Value.PrivateKeyPemBase64,
        options.Value.SigningKey, environment.IsDevelopment());

    public async Task<TokenResponse> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        TokenResponse response = await CreateTokenResponseAsync(user, Guid.NewGuid(), now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return response;
    }

    private async Task<TokenResponse> CreateTokenResponseAsync(ApplicationUser user, Guid familyId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        IList<string> roles = await userManager.GetRolesAsync(user);
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim())
        ];
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(RolePermissions.Resolve(roles).Select(permission => new Claim(Permissions.ClaimType, permission)));
        if (user.BusinessId.HasValue) claims.Add(new Claim("business_id", user.BusinessId.Value.ToString()));
        if (user.BranchId.HasValue) claims.Add(new Claim("branch_id", user.BranchId.Value.ToString()));
        if (user.CourierId.HasValue) claims.Add(new Claim("courier_id", user.CourierId.Value.ToString()));

        JwtSecurityToken jwt = new(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            new SigningCredentials(_signingKey, JwtKeyMaterial.AlgorithmFor(_signingKey)));

        string rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = Hash(rawRefreshToken),
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenDays)
        });

        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(jwt), rawRefreshToken, expiresAt);
    }

    // A rotated token presented again within this window is treated as a retry (lost response on a weak
    // mobile connection, or two app processes refreshing at once), not as theft.
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(60);

    public Task<TokenResponse?> RefreshAsync(string rawToken, CancellationToken cancellationToken) =>
        RefreshAsync(rawToken, allowConflictRetry: true, cancellationToken);

    private async Task<TokenResponse?> RefreshAsync(string rawToken, bool allowConflictRetry,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string tokenHash = Hash(rawToken);
        RefreshToken? token = await context.RefreshTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (token is null || !token.User.IsActive || token.ExpiresAtUtc <= now) return null;
        if (token.RevokedAtUtc is not null)
        {
            if (await IsGracefulReuseAsync(token, now, cancellationToken))
            {
                // Issue another token in the same family; the earlier replacement stays valid until it rotates.
                TokenResponse retryResponse = await CreateTokenResponseAsync(token.User, token.FamilyId, now, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                return retryResponse;
            }
            await RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            return null;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        token.RevokedAtUtc = now;
        TokenResponse response = await CreateTokenResponseAsync(token.User, token.FamilyId, now, cancellationToken);
        token.ReplacedByTokenHash = Hash(response.RefreshToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            // A concurrent refresh with the same token just rotated it; retry once through the grace path.
            if (allowConflictRetry) return await RefreshAsync(rawToken, allowConflictRetry: false, cancellationToken);
            await RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            return null;
        }
    }

    private async Task<bool> IsGracefulReuseAsync(RefreshToken token, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Only a token that was rotated (not logged out or revoked as part of a family) qualifies, and only
        // while its family still has a live token, so a family revoked for theft stays revoked.
        if (token.ReplacedByTokenHash is null || token.RevokedAtUtc is null ||
            now - token.RevokedAtUtc.Value > ReuseGracePeriod)
            return false;
        return await context.RefreshTokens.AnyAsync(x => x.FamilyId == token.FamilyId &&
            x.RevokedAtUtc == null && x.ExpiresAtUtc > now, cancellationToken);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken)
    {
        RefreshToken? token = await context.RefreshTokens.SingleOrDefaultAsync(
            x => x.TokenHash == Hash(rawToken), cancellationToken);
        if (token is null || token.RevokedAtUtc is not null) return;
        token.RevokedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
    }

    private Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.RefreshTokens.Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAtUtc, now), cancellationToken);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
