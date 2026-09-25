using Microsoft.AspNetCore.Identity;

namespace DeliveryOps.Auth.Api.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid? BusinessId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? CourierId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
