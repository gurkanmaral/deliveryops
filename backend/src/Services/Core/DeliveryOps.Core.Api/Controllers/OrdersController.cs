using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Queries.Orders;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize(Policy = Permissions.OrdersRead)]
public sealed class OrdersController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<OrderResponse>>> GetAll(
        [FromQuery] Guid? businessId,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? courierId,
        [FromQuery] OrderStatus? status,
        [FromQuery] OrderSource? source,
        [FromQuery] DateOnly? createdFrom,
        [FromQuery] DateOnly? createdTo,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] string sort = "-created",
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetOrdersQuery(businessId, branchId, courierId, status, source,
            createdFrom, createdTo, minAmount, maxAmount, page, pageSize, search, sort), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetOrderQuery(id), cancellationToken));

    [HttpGet("available")]
    [Authorize(Policy = Permissions.OrdersClaim)]
    public async Task<ActionResult<PagedResponse<AvailableOrderResponse>>> Available([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetAvailableOrdersQuery(page, pageSize), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.OrdersWrite)]
    [EnableRateLimiting("order-creation")]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken cancellationToken)
    {
        OrderSource source = User.IsInRole("PlatformAdmin") ? OrderSource.AdminPanel : OrderSource.BusinessPanel;
        var result = await sender.Send(new CreateOrderCommand(request.BusinessId, request.BranchId, request.ExternalId ?? string.Empty,
            request.CustomerName, request.CustomerPhone, request.DeliveryAddress, source, request.TotalAmount, idempotencyKey,
            DeliveryLatitude: request.DeliveryLatitude, DeliveryLongitude: request.DeliveryLongitude,
            DeliveryInstructions: request.DeliveryInstructions,
            DeliveryLocationSource: request.DeliveryLatitude.HasValue ? DeliveryLocationSource.MapPin : DeliveryLocationSource.Unknown,
            DeliveryLocationAccuracy: request.DeliveryLatitude.HasValue ? DeliveryLocationAccuracy.Exact : DeliveryLocationAccuracy.Unknown), cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/orders/{result.Value!.Id}", result.Value) : FromResult(result);
    }

    [HttpPost("phone")]
    [Authorize(Policy = Permissions.OrdersWrite)]
    [EnableRateLimiting("order-creation")]
    public async Task<ActionResult<OrderResponse>> CreatePhoneOrder(CreatePhoneOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateOrderCommand(request.BusinessId, request.BranchId, string.Empty,
            request.CustomerName, request.CustomerPhone, request.DeliveryAddress, OrderSource.Phone, request.TotalAmount,
            idempotencyKey, DeliveryLatitude: request.DeliveryLatitude, DeliveryLongitude: request.DeliveryLongitude,
            DeliveryInstructions: request.DeliveryInstructions,
            DeliveryLocationSource: request.DeliveryLatitude.HasValue ? DeliveryLocationSource.MapPin : DeliveryLocationSource.Unknown,
            DeliveryLocationAccuracy: request.DeliveryLatitude.HasValue ? DeliveryLocationAccuracy.Exact : DeliveryLocationAccuracy.Unknown), cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/orders/{result.Value!.Id}", result.Value) : FromResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.OrdersWrite)]
    public async Task<ActionResult<OrderResponse>> Update(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateOrderCommand(id, request.CustomerName, request.CustomerPhone, request.DeliveryAddress, request.TotalAmount), cancellationToken));

    [HttpPut("{id:guid}/courier")]
    [Authorize(Policy = Permissions.OrdersAssign)]
    public async Task<ActionResult<OrderResponse>> AssignCourier(Guid id, AssignOrderCourierRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new AssignOrderCourierCommand(id, request.CourierId), cancellationToken));

    [HttpPost("{id:guid}/claim")]
    [Authorize(Policy = Permissions.OrdersClaim)]
    public async Task<ActionResult<OrderResponse>> Claim(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new ClaimOrderCommand(id), cancellationToken));

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Permissions.OrdersTransition)]
    public async Task<ActionResult<OrderResponse>> ChangeStatus(Guid id, ChangeOrderStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new ChangeOrderStatusCommand(id, request.Status), cancellationToken));

    [HttpPost("{id:guid}/delivery-failure")]
    [Authorize(Policy = Permissions.OrdersTransition)]
    public async Task<ActionResult<OrderResponse>> DeliveryFailure(Guid id, DeliveryFailureRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new ReportDeliveryFailureCommand(id, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Permissions.OrdersWrite)]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid id, CancelOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new CancelOrderCommand(id, request.Reason), cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.OrdersWrite)]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new CancelOrderCommand(id, null), cancellationToken));
}

public sealed record CreateOrderRequest(Guid BusinessId, Guid BranchId, string? ExternalId, string CustomerName, string CustomerPhone,
    string DeliveryAddress, decimal TotalAmount, double? DeliveryLatitude = null, double? DeliveryLongitude = null,
    string? DeliveryInstructions = null);
public sealed record CreatePhoneOrderRequest(Guid BusinessId, Guid BranchId, string CustomerName, string CustomerPhone,
    string DeliveryAddress, decimal TotalAmount, double? DeliveryLatitude = null, double? DeliveryLongitude = null,
    string? DeliveryInstructions = null);
public sealed record UpdateOrderRequest(string CustomerName, string CustomerPhone, string DeliveryAddress, decimal TotalAmount);
public sealed record AssignOrderCourierRequest(Guid CourierId);
public sealed record ChangeOrderStatusRequest(OrderStatus Status);
public sealed record CancelOrderRequest(string? Reason);
public sealed record DeliveryFailureRequest(string Reason);
