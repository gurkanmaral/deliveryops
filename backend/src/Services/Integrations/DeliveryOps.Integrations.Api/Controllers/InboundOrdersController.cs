using System.Text;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using DeliveryOps.Integrations.Api.Security;
using DeliveryOps.Integrations.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace DeliveryOps.Integrations.Api.Controllers;

[ApiController]
[Route("api/v1/webhooks/{connectionId:guid}/orders")]
[AllowAnonymous]
public sealed class InboundOrdersController(IntegrationsDbContext context,
    ProviderAdapterRegistry adapterRegistry, WebhookAuthenticator authenticator,
    InboundEventIngestor ingestor) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(1_048_576)]
    [EnableRateLimiting("provider-webhooks")]
    public async Task<ActionResult<InboundOrderResponse>> Receive(Guid connectionId,
        CancellationToken cancellationToken)
    {
        IntegrationConnection? connection = await context.Connections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == connectionId && item.IsActive, cancellationToken);
        if (connection is null) return NotFound();
        using StreamReader reader = new(Request.Body, Encoding.UTF8);
        string rawPayload = await reader.ReadToEndAsync(cancellationToken);
        if (!authenticator.Verify(Request, connection, rawPayload)) return Unauthorized();
        AdaptedOrder adapted;
        try { adapted = adapterRegistry.Resolve(connection.Provider, connection.AdapterVersion).Adapt(rawPayload); }
        catch (ProviderPayloadException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                { ["payload"] = [exception.Message] }));
        }

        IngestResult result = await ingestor.IngestAsync(connection, adapted, rawPayload, cancellationToken);
        return result.Outcome switch
        {
            IngestOutcome.Accepted => Accepted(Map(result.Event!, false)),
            IngestOutcome.Conflict => Conflict(new ProblemDetails { Title = "Webhook olayı çakışıyor.",
                Detail = "Aynı olay numarası farklı bir payload ile daha önce alınmış.", Status = 409 }),
            _ => StatusCode(result.Event!.Status == InboundEventStatus.Completed ? 200 : 202, Map(result.Event, true))
        };
    }

    private static InboundOrderResponse Map(InboundOrderEvent item, bool duplicate) =>
        new(item.Id, item.CoreOrderId, duplicate, item.Status, item.NextAttemptAtUtc);
}

public sealed record InboundOrderResponse(Guid EventId, Guid? OrderId, bool Duplicate,
    InboundEventStatus Status, DateTimeOffset? NextAttemptAtUtc);
