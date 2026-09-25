using DeliveryOps.BuildingBlocks.Application;

namespace DeliveryOps.Core.Handlers.Common;

internal static class HandlerErrors
{
    public static Error NotFound(string resource) => new("not_found", $"{resource} bulunamadı.");
    public static Error Forbidden => new("forbidden", "Bu kayıt üzerinde işlem yetkiniz yok.");
    public static Error Conflict(string message) => new("conflict", message);
    public static Error Validation(string message) => new("validation", message);
}
