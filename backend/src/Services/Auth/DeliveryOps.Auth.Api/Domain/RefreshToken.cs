namespace DeliveryOps.Auth.Api.Domain;

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public ApplicationUser User { get; init; } = null!;
    public Guid FamilyId { get; init; }
    public string TokenHash { get; init; } = string.Empty;
    public string? ReplacedByTokenHash { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public uint Version { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAtUtc is null && ExpiresAtUtc > now;
}
