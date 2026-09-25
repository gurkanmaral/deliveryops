namespace DeliveryOps.Core.Queries.Abstractions;

public interface IRequestContext
{
    Guid UserId { get; }
    Guid? BusinessId { get; }
    Guid? BranchId { get; }
    Guid? CourierId { get; }
    bool IsPlatformAdmin { get; }
}
