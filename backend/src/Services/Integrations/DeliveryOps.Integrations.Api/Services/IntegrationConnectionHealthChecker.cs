using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class IntegrationConnectionHealthChecker(
    IntegrationsDbContext context,
    YemeksepetiPartnerClient yemeksepetiPartnerClient,
    GetirFoodClient getirFoodClient,
    CoreOrdersClient coreOrdersClient,
    TimeProvider timeProvider,
    IOptions<IntegrationHealthCheckOptions> options,
    ILogger<IntegrationConnectionHealthChecker> logger)
{
    public async Task<IntegrationConnectionHealthResult> CheckAsync(IntegrationConnection connection,
        bool automatic, CancellationToken cancellationToken)
    {
        DateTimeOffset checkedAt = timeProvider.GetUtcNow();
        bool success;
        DateTimeOffset? tokenExpiresAt = null;
        string message;
        try
        {
            if (connection.Provider == IntegrationProvider.Getir)
            {
                GetirLoginResult result = await getirFoodClient.TestConnectionAsync(connection, cancellationToken);
                tokenExpiresAt = result.ExpiresAtUtc;
                message = "Getir anahtarları doğrulandı.";
            }
            else
            {
                YemeksepetiConnectionTestResult result = await yemeksepetiPartnerClient
                    .TestConnectionAsync(connection, cancellationToken);
                tokenExpiresAt = result.TokenExpiresAtUtc;
                message = "OAuth kimlik bilgileri doğrulandı.";
            }
            success = true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            success = false;
            message = GetSafeMessage(exception, connection.Provider == IntegrationProvider.Getir ? "Getir" : "Yemeksepeti");
            logger.LogWarning("Integration connection {ConnectionId} health check failed: {Message}",
                connection.Id, message);
        }

        connection.RecordHealthCheck(success, message, checkedAt, tokenExpiresAt, automatic);
        context.HealthChecks.Add(IntegrationHealthCheck.Create(connection.Id, success, message,
            connection.ProviderEnvironment, automatic, checkedAt, tokenExpiresAt));
        await context.SaveChangesAsync(cancellationToken);

        int alertThreshold = Math.Max(1, options.Value.AlertAfterFailures);
        if (success || connection.ConsecutiveHealthCheckFailures >= alertThreshold)
        {
            try
            {
                await coreOrdersClient.SetIntegrationHealthAsync(connection, success, message,
                    checkedAt, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception,
                    "Could not publish integration connection {ConnectionId} health to Core API.",
                    connection.Id);
            }
        }

        return new IntegrationConnectionHealthResult(success, connection.ProviderEnvironment,
            checkedAt, tokenExpiresAt, message, connection.ConsecutiveHealthCheckFailures);
    }

    private static string GetSafeMessage(Exception exception, string provider) => exception switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest or
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } =>
            $"{provider} kimlik bilgileri doğrulanamadı.",
        HttpRequestException { StatusCode: not null } httpException =>
            $"{provider} servisi {(int)httpException.StatusCode.Value} durum kodu döndürdü.",
        HttpRequestException => $"{provider} servisine ulaşılamadı.",
        TaskCanceledException => $"{provider} servisi zaman aşımına uğradı.",
        _ => "Kimlik bilgileri doğrulanamadı. Ayarları kontrol edin."
    };
}

public sealed record IntegrationConnectionHealthResult(bool Success, ProviderEnvironment Environment,
    DateTimeOffset CheckedAtUtc, DateTimeOffset? TokenExpiresAtUtc, string Message,
    int ConsecutiveFailures);

public sealed class IntegrationHealthCheckOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 15;
    public int ScanIntervalSeconds { get; init; } = 60;
    public int AlertAfterFailures { get; init; } = 2;
}
