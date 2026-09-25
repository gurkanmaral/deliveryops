using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Api.Reports;
using DeliveryOps.Core.Queries.Reports;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Permissions.OrdersRead)]
public sealed class ReportsController(ISender sender) : ApiControllerBase
{
    [HttpGet("operations")]
    [ProducesResponseType<OperationsReportResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationsReportResponse>> GetOperations(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? businessId,
        CancellationToken cancellationToken)
    {
        (DateOnly fromDate, DateOnly toDate) = ResolveDates(from, to);
        return FromResult(await sender.Send(new GetOperationsReportQuery(fromDate, toDate, businessId), cancellationToken));
    }

    [HttpGet("operations/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/csv")]
    public async Task<ActionResult> ExportOperations(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? businessId,
        [FromQuery] string format = "xlsx",
        CancellationToken cancellationToken = default)
    {
        (DateOnly fromDate, DateOnly toDate) = ResolveDates(from, to);
        Result<OperationsReportResponse> result = await sender.Send(
            new GetOperationsReportQuery(fromDate, toDate, businessId), cancellationToken);
        if (result.IsFailure) return FromResult(Result.Failure(result.Error));

        OperationsReportResponse report = result.Value!;
        string suffix = $"{report.From:yyyyMMdd}-{report.To:yyyyMMdd}";
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return File(OperationsReportExporter.ToCsv(report), "text/csv; charset=utf-8", $"operasyon-raporu-{suffix}.csv");
        if (!string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "validation", Detail = "Format xlsx veya csv olmalıdır.", Status = 400 });
        return File(OperationsReportExporter.ToExcel(report),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"operasyon-raporu-{suffix}.xlsx");
    }

    private static (DateOnly From, DateOnly To) ResolveDates(DateOnly? from, DateOnly? to)
    {
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            DateTimeOffset.UtcNow, "Europe/Istanbul").DateTime);
        return (from ?? today.AddDays(-29), to ?? today);
    }
}
