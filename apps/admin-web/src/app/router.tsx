import { createBrowserRouter } from 'react-router-dom'
import { lazy, Suspense, type ReactNode } from 'react'
import { AppLayout } from '../components/layout/AppLayout'
import { LoginPage } from '../features/auth/LoginPage'
import { RequireAuth } from '../features/auth/RequireAuth'
import { RequirePermission } from '../features/auth/RequirePermission'

const DashboardPage = lazy(() => import('../features/dashboard/pages/DashboardPage').then(module => ({ default: module.DashboardPage })))
const BusinessesPage = lazy(() => import('../features/businesses/BusinessesPage').then(module => ({ default: module.BusinessesPage })))
const CouriersPage = lazy(() => import('../features/couriers/CouriersPage').then(module => ({ default: module.CouriersPage })))
const OrdersPage = lazy(() => import('../features/orders/OrdersPage').then(module => ({ default: module.OrdersPage })))
const UsersPage = lazy(() => import('../features/users/UsersPage').then(module => ({ default: module.UsersPage })))
const IntegrationsPage = lazy(() => import('../features/integrations/IntegrationsPage').then(module => ({ default: module.IntegrationsPage })))
const DispatchSettingsPage = lazy(() => import('../features/settings/DispatchSettingsPage').then(module => ({ default: module.DispatchSettingsPage })))
const AlertsPage = lazy(() => import('../features/alerts/AlertsPage').then(module => ({ default: module.AlertsPage })))
const ReportsPage = lazy(() => import('../features/reports/ReportsPage').then(module => ({ default: module.ReportsPage })))
const BillingPage = lazy(() => import('../features/billing/BillingPage').then(module => ({ default: module.BillingPage })))
const OutboxPage = lazy(() => import('../features/outbox/OutboxPage').then(module => ({ default: module.OutboxPage })))

function deferred(page: ReactNode) {
  return <Suspense fallback={<div className="api-notice">Yükleniyor…</div>}>{page}</Suspense>
}

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <LoginPage />,
  },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { index: true, element: deferred(<DashboardPage />) },
          { path: 'orders', element: <RequirePermission permission="orders.read">{deferred(<OrdersPage />)}</RequirePermission> },
          { path: 'couriers', element: <RequirePermission permission="couriers.read">{deferred(<CouriersPage />)}</RequirePermission> },
          { path: 'businesses', element: <RequirePermission permission="businesses.read">{deferred(<BusinessesPage />)}</RequirePermission> },
          { path: 'users', element: <RequirePermission permission="users.read">{deferred(<UsersPage />)}</RequirePermission> },
          { path: 'integrations', element: <RequirePermission permission="integrations.read">{deferred(<IntegrationsPage />)}</RequirePermission> },
          { path: 'outbox', element: <RequirePermission permission="integrations.read">{deferred(<OutboxPage />)}</RequirePermission> },
          { path: 'alerts', element: <RequirePermission permission="dispatch.read">{deferred(<AlertsPage />)}</RequirePermission> },
          { path: 'reports', element: <RequirePermission permission="orders.read">{deferred(<ReportsPage />)}</RequirePermission> },
          { path: 'billing', element: <RequirePermission permission="billing.read">{deferred(<BillingPage />)}</RequirePermission> },
          { path: 'settings', element: <RequirePermission permission="dispatch.read">{deferred(<DispatchSettingsPage />)}</RequirePermission> },
        ],
      },
    ],
  },
])
