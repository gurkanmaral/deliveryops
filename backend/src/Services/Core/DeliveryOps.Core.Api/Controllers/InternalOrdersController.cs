using DeliveryOps.Core.Api.Security;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Orders;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/internal/orders")]
[Authorize(Policy = InternalPermissions.OrdersIngest)]
public sealed class InternalOrdersController(ISender sender, CoreDbContext context) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InternalOrderResponse>> Create(InternalCreateOrderRequest request, CancellationToken cancellationToken)
    {
        Guid? existingId = await FindExistingId(request, cancellationToken);
        if (existingId.HasValue) return Ok(new InternalOrderResponse(existingId.Value, true));

        var result = await sender.Send(new CreateOrderCommand(request.BusinessId, request.BranchId, request.ExternalId,
            request.CustomerName, request.CustomerPhone, request.DeliveryAddress, request.Source, request.TotalAmount,
            CreateIdempotencyKey(request), IsTrustedIntegration: true, request.DeliveryLatitude,
            request.DeliveryLongitude, request.DeliveryInstructions,
            request.DeliveryLatitude.HasValue ? DeliveryLocationSource.Provider : DeliveryLocationSource.Unknown,
            request.DeliveryLatitude.HasValue ? DeliveryLocationAccuracy.Exact : DeliveryLocationAccuracy.Unknown,
            request.DeliveryFulfillment, request.Payment?.ToInput()), cancellationToken);
        if (result.IsSuccess)
            return Created($"/api/v1/orders/{result.Value!.Id}", new InternalOrderResponse(result.Value.Id, false));

        existingId = await FindExistingId(request, cancellationToken);
        if (existingId.HasValue) return Ok(new InternalOrderResponse(existingId.Value, true));
        int status = result.Error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "forbidden" => StatusCodes.Status403Forbidden,
            "validation" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict
        };
        return StatusCode(status, new ProblemDetails { Title = result.Error.Code, Detail = result.Error.Message, Status = status });
    }

    [HttpPost("provider-event")]
    public async Task<ActionResult<InternalProviderEventResponse>> ApplyProviderEvent(
        InternalProviderOrderEventRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ApplyProviderOrderEventCommand(request.BusinessId,
            request.ExternalOrderId, request.Source, request.ExternalEventId, request.ProviderStatus,
            request.CancellationReason, request.Payment?.ToInput()), cancellationToken);
        if (result.IsSuccess)
            return Ok(new InternalProviderEventResponse(result.Value!.OrderId, result.Value.Duplicate,
                result.Value.Status, result.Value.CreditRefunded, result.Value.Outcome));
        int status = result.Error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "forbidden" => StatusCodes.Status403Forbidden,
            "validation" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict
        };
        return StatusCode(status, new ProblemDetails
            { Title = result.Error.Code, Detail = result.Error.Message, Status = status });
    }

    private Task<Guid?> FindExistingId(InternalCreateOrderRequest request, CancellationToken cancellationToken) =>
        context.Orders.AsNoTracking()
            .Where(order => order.BusinessId == request.BusinessId && order.Source == request.Source && order.ExternalId == request.ExternalId.Trim())
            .Select(order => (Guid?)order.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private static string CreateIdempotencyKey(InternalCreateOrderRequest request) => "integration-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{(int)request.Source}:{request.ExternalId.Trim()}")));
}

public sealed record InternalCreateOrderRequest(Guid BusinessId, Guid BranchId, string ExternalId,
    string CustomerName, string CustomerPhone, string DeliveryAddress, OrderSource Source, decimal TotalAmount,
    double? DeliveryLatitude = null, double? DeliveryLongitude = null, string? DeliveryInstructions = null,
    DeliveryFulfillmentType DeliveryFulfillment = DeliveryFulfillmentType.MerchantCourier,
    InternalOrderPayment? Payment = null);
public sealed record InternalOrderPayment(PaymentMethod Method, bool IsPaid = false, decimal? Amount = null,
    string? Reference = null, DateTimeOffset? PaidAtUtc = null)
{
    public OrderPaymentInput ToInput() => new(Method, IsPaid, Amount, Reference, PaidAtUtc);
}
public sealed record InternalOrderResponse(Guid Id, bool Duplicate);
public sealed record InternalProviderOrderEventRequest(Guid BusinessId, string ExternalOrderId,
    OrderSource Source, string ExternalEventId, string ProviderStatus, string? CancellationReason,
    InternalOrderPayment? Payment = null);
public sealed record InternalProviderEventResponse(Guid Id, bool Duplicate, OrderStatus Status,
    bool CreditRefunded, string Outcome);
