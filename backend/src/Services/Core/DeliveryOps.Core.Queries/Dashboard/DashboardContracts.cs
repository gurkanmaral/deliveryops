using MediatR;

namespace DeliveryOps.Core.Queries.Dashboard;

public sealed record DashboardSummary(
    int ActiveBusinesses,
    int ActiveCouriers,
    int OpenOrders,
    int DeliveringCouriers);

public sealed record GetDashboardSummaryQuery : IRequest<DashboardSummary>;
