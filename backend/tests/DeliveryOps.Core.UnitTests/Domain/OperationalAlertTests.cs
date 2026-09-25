using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class OperationalAlertTests
{
    [Fact]
    public void Resolved_integration_alert_reopens_when_connection_fails_again()
    {
        DateTimeOffset now = new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
        OperationalAlert alert = OperationalAlert.Create(Guid.NewGuid(), null, null,
            $"integration:{Guid.NewGuid()}:health",
            OperationalAlertType.IntegrationConnectionUnavailable,
            OperationalAlertSeverity.Critical, "Yemeksepeti bağlantısı çalışmıyor",
            "Client bilgileri doğrulanamadı.", now);

        Assert.True(alert.Resolve(now.AddMinutes(5)));
        Assert.True(alert.Refresh(OperationalAlertSeverity.Critical,
            "Yemeksepeti bağlantısı çalışmıyor", "Client bilgileri doğrulanamadı.",
            now.AddMinutes(10)));

        Assert.Equal(OperationalAlertStatus.Active, alert.Status);
        Assert.Null(alert.ResolvedAtUtc);
    }

    [Fact]
    public void Alert_EscalatesAndResolvesWithoutCreatingANewIdentity()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationalAlert alert = OperationalAlert.Create(Guid.NewGuid(), Guid.NewGuid(), null,
            "order:test:waiting", OperationalAlertType.CourierWaiting, OperationalAlertSeverity.Warning,
            "Kurye ataması gecikti", "Sipariş 5 dakikadır bekliyor.", now);

        bool escalated = alert.Refresh(OperationalAlertSeverity.Critical, "Kurye ataması gecikti",
            "Sipariş 10 dakikadır bekliyor.", now.AddMinutes(5));
        bool resolved = alert.Resolve(now.AddMinutes(6));

        Assert.True(escalated);
        Assert.True(resolved);
        Assert.Equal(OperationalAlertSeverity.Critical, alert.Severity);
        Assert.Equal(OperationalAlertStatus.Resolved, alert.Status);
    }

    [Fact]
    public void Alert_ReopensWhenResolvedProblemReturns()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationalAlert alert = OperationalAlert.Create(Guid.NewGuid(), null, Guid.NewGuid(),
            "courier:test:location", OperationalAlertType.CourierLocationStale,
            OperationalAlertSeverity.Warning, "Konum eski", "Konum alınamadı.", now);
        alert.Resolve(now.AddMinutes(1));

        bool changed = alert.Refresh(OperationalAlertSeverity.Warning, "Konum eski", "Konum alınamadı.", now.AddMinutes(2));

        Assert.True(changed);
        Assert.Equal(OperationalAlertStatus.Active, alert.Status);
        Assert.Null(alert.ResolvedAtUtc);
    }

    [Fact]
    public void SlaSettings_RequiresCriticalThresholdAboveWarning()
    {
        BusinessSlaSettings settings = BusinessSlaSettings.CreateDefault(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Update(5, 5, 10, 20, 30, 45, 3, 10));
    }
}
