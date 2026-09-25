using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Api.Billing;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Queries.Billing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/billing")]
public sealed class BillingController(ISender sender) : ApiControllerBase
{
    [HttpGet("settings")]
    [Authorize(Policy = Permissions.BillingRead)]
    public async Task<ActionResult<BillingSettingsResponse>> GetSettings([FromQuery] Guid? businessId,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetBillingSettingsQuery(businessId), cancellationToken));

    [HttpPut("settings")]
    [Authorize(Policy = Permissions.BillingWrite)]
    public async Task<ActionResult<BillingSettingsResponse>> UpdateSettings(UpdateBillingSettingsRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateBillingSettingsCommand(request.BusinessId,
            request.FeePerDeliveredOrder, request.CommissionRatePercent,
            request.FeePerReturnedOrder, request.TaxRatePercent), cancellationToken));

    [HttpGet("preview")]
    [Authorize(Policy = Permissions.BillingRead)]
    public async Task<ActionResult<BillingPreviewResponse>> GetPreview([FromQuery] Guid? businessId,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetBillingPreviewQuery(businessId, from, to), cancellationToken));

    [HttpGet("settlements")]
    [Authorize(Policy = Permissions.BillingRead)]
    public async Task<ActionResult<PagedResponse<BillingSettlementResponse>>> GetSettlements(
        [FromQuery] Guid? businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetBillingSettlementsQuery(businessId, page, pageSize), cancellationToken));

    [HttpPost("settlements")]
    [Authorize(Policy = Permissions.BillingWrite)]
    public async Task<ActionResult<BillingSettlementResponse>> CreateSettlement(CreateBillingSettlementRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new CreateBillingSettlementCommand(request.BusinessId,
            request.From, request.To), cancellationToken));

    [HttpPost("settlements/{id:guid}/finalize")]
    [Authorize(Policy = Permissions.BillingWrite)]
    public async Task<ActionResult<BillingSettlementResponse>> FinalizeSettlement(Guid id,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new FinalizeBillingSettlementCommand(id), cancellationToken));

    [HttpGet("settlements/{id:guid}/document")]
    [Authorize(Policy = Permissions.BillingRead)]
    [Produces("application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<ActionResult> DownloadDocument(Guid id, [FromQuery] string format = "pdf",
        CancellationToken cancellationToken = default)
    {
        Result<BillingSettlementResponse> result = await sender.Send(new GetBillingSettlementQuery(id), cancellationToken);
        if (result.IsFailure) return FromResult(Result.Failure(result.Error));
        BillingSettlementResponse settlement = result.Value!;
        if (settlement.Status != BillingSettlementStatus.Finalized)
            return Conflict(new ProblemDetails { Title = "conflict", Detail = "Belge yalnızca kesinleşmiş mutabakat için oluşturulabilir.", Status = 409 });
        string safeNumber = settlement.DocumentNumber.ToLowerInvariant();
        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            return File(BillingDocumentExporter.ToPdf(settlement), "application/pdf", $"{safeNumber}.pdf");
        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            return File(BillingDocumentExporter.ToExcel(settlement),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{safeNumber}.xlsx");
        return BadRequest(new ProblemDetails { Title = "validation", Detail = "Format pdf veya xlsx olmalıdır.", Status = 400 });
    }
}

public sealed record UpdateBillingSettingsRequest(Guid? BusinessId, decimal FeePerDeliveredOrder,
    decimal CommissionRatePercent, decimal FeePerReturnedOrder, decimal TaxRatePercent);
public sealed record CreateBillingSettlementRequest(Guid? BusinessId, DateOnly From, DateOnly To);
