using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Queries.Credits;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/credits")]
public sealed class CreditsController(ISender sender) : ApiControllerBase
{
    [HttpGet("packages")]
    [Authorize(Policy = Permissions.CreditsRead)]
    public async Task<ActionResult<PagedResponse<CreditPackageResponse>>> GetPackages(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetCreditPackagesQuery(page, pageSize), cancellationToken));

    [HttpGet("account")]
    [Authorize(Policy = Permissions.CreditsRead)]
    public async Task<ActionResult<CreditAccountResponse>> GetAccount([FromQuery] Guid? businessId,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetCreditAccountQuery(businessId), cancellationToken));

    [HttpGet("transactions")]
    [Authorize(Policy = Permissions.CreditsRead)]
    public async Task<ActionResult<PagedResponse<CreditTransactionResponse>>> GetTransactions(
        [FromQuery] Guid? businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetCreditTransactionsQuery(businessId, page, pageSize), cancellationToken));

    [HttpPost("top-up")]
    [Authorize(Policy = Permissions.CreditsWrite)]
    public async Task<ActionResult<CreditAccountResponse>> TopUp(TopUpCreditsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new TopUpCreditsCommand(request.BusinessId,
            request.PackageCode, request.Description, idempotencyKey), cancellationToken));

    [HttpPost("adjustments")]
    [Authorize(Policy = Permissions.CreditsWrite)]
    public async Task<ActionResult<CreditAccountResponse>> Adjust(AdjustCreditsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new AdjustCreditsCommand(request.BusinessId,
            request.Amount, request.Description, idempotencyKey), cancellationToken));

    [HttpPost("refunds")]
    [Authorize(Policy = Permissions.CreditsWrite)]
    public async Task<ActionResult<CreditAccountResponse>> Refund(RefundOrderCreditRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new RefundOrderCreditCommand(request.BusinessId,
            request.OrderId, request.Description, idempotencyKey), cancellationToken));

    [HttpPut("settings")]
    [Authorize(Policy = Permissions.CreditsWrite)]
    public async Task<ActionResult<CreditAccountResponse>> UpdateSettings(UpdateCreditSettingsRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateCreditSettingsCommand(request.BusinessId,
            request.LowBalanceThreshold), cancellationToken));
}

public sealed record TopUpCreditsRequest(Guid? BusinessId, string PackageCode, string? Description);
public sealed record AdjustCreditsRequest(Guid? BusinessId, int Amount, string Description);
public sealed record RefundOrderCreditRequest(Guid? BusinessId, Guid OrderId, string Description);
public sealed record UpdateCreditSettingsRequest(Guid? BusinessId, int LowBalanceThreshold);
