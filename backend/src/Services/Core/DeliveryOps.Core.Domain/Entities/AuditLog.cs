using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class AuditLog : Entity
{
    private AuditLog() { }
    public Guid UserId { get; private init; }
    public Guid? BusinessId { get; private init; }
    public string Action { get; private init; } = string.Empty;
    public string EntityName { get; private init; } = string.Empty;
    public string EntityId { get; private init; } = string.Empty;
    public string ChangesJson { get; private init; } = "{}";

    public static AuditLog Create(Guid userId, Guid? businessId, string action, string entityName, string entityId, string changesJson) =>
        new() { UserId = userId, BusinessId = businessId, Action = action, EntityName = entityName, EntityId = entityId, ChangesJson = changesJson };
}
