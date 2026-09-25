import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, Bike, Building2, PackageCheck, PackageOpen, Plus, Store } from 'lucide-react'
import { Link } from 'react-router-dom'
import { lazy, Suspense } from 'react'
import { useAuth } from '../../auth/AuthProvider'
import { getJson } from '../../../shared/api/httpClient'
import type { PagedResponse } from '../../../shared/api/types'
import { useCourierLocations } from '../api/useCourierLocations'
import { useDashboardSummary } from '../api/useDashboardSummary'
import type { OperationalAlert } from '../../alerts/AlertsPage'

const CourierMap = lazy(() => import('../components/CourierMap').then(module => ({ default: module.CourierMap })))

interface DashboardOrder {
  id: string
  businessId: string
  courierId?: string
  customerName: string
  deliveryAddress: string
  status: number
  createdAtUtc: string
}

interface DashboardBusiness { id: string; name: string; branchCount: number }
interface DashboardCourier { id: string; firstName: string; lastName: string }

const statusLabels = ['Yeni', 'Onaylandı', 'Kurye bekliyor', 'Atandı', 'Teslim alındı', 'Yolda', 'Teslim edildi', 'İptal', 'Teslim edilemedi', 'İade']
const terminalStatuses = new Set([6, 7, 8, 9])

export function DashboardPage() {
  const auth = useAuth()
  const summary = useDashboardSummary()
  const locations = useCourierLocations()
  const orders = useQuery({
    queryKey: ['orders', 'dashboard'],
    queryFn: ({ signal }) => getJson<PagedResponse<DashboardOrder>>('/api/v1/orders?pageSize=100', signal),
  })
  const businesses = useQuery({
    queryKey: ['businesses'],
    queryFn: ({ signal }) => getJson<PagedResponse<DashboardBusiness>>('/api/v1/businesses?pageSize=100', signal),
  })
  const couriers = useQuery({
    queryKey: ['couriers', 'dashboard'],
    queryFn: ({ signal }) => getJson<PagedResponse<DashboardCourier>>('/api/v1/couriers?pageSize=100', signal),
  })
  const alertPath = auth.user?.businessId ? `/api/v1/operations/alerts?businessId=${auth.user.businessId}` : '/api/v1/operations/alerts'
  const alerts = useQuery({ queryKey: ['operational-alerts', 'dashboard', auth.user?.businessId], enabled: auth.hasPermission('dispatch.read'), queryFn: ({ signal }) => getJson<PagedResponse<OperationalAlert>>(`${alertPath}${alertPath.includes('?') ? '&' : '?'}pageSize=100`, signal) })

  const orderItems = orders.data?.items ?? []
  const terminalOrders = orderItems.filter(order => terminalStatuses.has(order.status))
  const deliveredCount = terminalOrders.filter(order => order.status === 6).length
  const successRate = terminalOrders.length === 0 ? 100 : Math.round(deliveredCount / terminalOrders.length * 100)
  const statusCounts = {
    delivered: orderItems.filter(order => order.status === 6).length,
    active: orderItems.filter(order => !terminalStatuses.has(order.status) && order.status !== 2).length,
    waiting: orderItems.filter(order => order.status === 2).length,
    failed: orderItems.filter(order => order.status === 7 || order.status === 8 || order.status === 9).length,
  }
  const businessNames = new Map((businesses.data?.items ?? []).map(item => [item.id, item.name]))
  const courierNames = new Map((couriers.data?.items ?? []).map(item => [item.id, `${item.firstName} ${item.lastName}`]))
  const ownBusiness = businesses.data?.items.find(item => item.id === auth.user?.businessId)
  const recentOrders = [...orderItems]
    .sort((left, right) => Date.parse(right.createdAtUtc) - Date.parse(left.createdAtUtc))
    .slice(0, 6)
  const hasError = summary.isError || locations.isError || orders.isError || businesses.isError || couriers.isError

  const metrics = [
    { label: 'Açık sipariş', value: summary.data?.openOrders, detail: 'Operasyondaki toplam paket', icon: PackageOpen },
    { label: 'Aktif kurye', value: summary.data?.activeCouriers, detail: 'Şu anda mesaide', icon: Bike },
    auth.isPlatformAdmin
      ? { label: 'Aktif işletme', value: summary.data?.activeBusinesses, detail: 'Sisteme bağlı işletme', icon: Building2 }
      : { label: 'Şube', value: ownBusiness?.branchCount, detail: ownBusiness?.name ?? 'İşletmenize bağlı', icon: Store },
    { label: 'Teslimatta', value: summary.data?.deliveringCouriers, detail: 'Paket götüren kurye', icon: PackageCheck },
  ]

  return <section>
    <div className="page-heading">
      <div><p className="eyebrow">{auth.isPlatformAdmin ? 'OPERASYON MERKEZİ' : 'İŞLETME OPERASYONU'}</p><h1>Genel bakış</h1><p>Sipariş, kurye ve teslimat ağının anlık görünümü.</p></div>
      {auth.hasPermission('orders.write') && <Link className="primary-button link-button" to="/orders"><Plus size={17} /> Yeni sipariş</Link>}
    </div>

    {hasError && <div className="api-notice">Bazı operasyon verileri şu anda alınamadı. Servis bağlantılarını kontrol edin.</div>}

    <div className="metric-grid">
      {metrics.map(({ label, value, detail, icon: Icon }) => <article className="metric-card" key={label}>
        <div className="metric-card__top"><span>{label}</span><span className="metric-card__icon"><Icon size={18} /></span></div>
        <strong className={value === undefined ? 'loading-value' : undefined}>{value ?? '—'}</strong>
        <small>{detail}</small>
      </article>)}
    </div>

    {(alerts.data?.totalCount ?? 0) > 0 && <article className="panel dashboard-alerts">
      <div className="panel__heading"><div><h2><AlertTriangle size={17} className="inline-icon" /> Müdahale gerekenler</h2><p>{alerts.data?.items.filter(item => item.severity === 2).length ?? 0} kritik uyarı</p></div><Link className="text-button" to="/alerts">Bildirim merkezini aç</Link></div>
      <div className="dashboard-alert-list">{alerts.data?.items.slice(0, 4).map(alert => <div key={alert.id}><i className={`dot ${alert.severity === 2 ? 'dot--red' : 'dot--amber'}`} /><span><strong>{alert.title}</strong><small>{alert.message}</small></span></div>)}</div>
    </article>}

    <div className="dashboard-grid">
      <article className="panel">
        <div className="panel__heading"><div><h2>Canlı kurye haritası</h2><p>{locations.data?.totalCount ?? 0} kuryeden konum alındı</p></div><Link className="text-button" to="/couriers">Kuryeleri gör</Link></div>
        <div className="map-placeholder"><Suspense fallback={<div className="map-empty">Harita yükleniyor…</div>}><CourierMap locations={locations.data?.items ?? []} /></Suspense><div className="map-legend"><span><i className="dot dot--green" /> Müsait</span><span><i className="dot dot--blue" /> Teslimatta</span><span><i className="dot" /> Eski konum</span></div></div>
      </article>

      <article className="panel status-panel">
        <div className="panel__heading"><div><h2>Teslimat durumu</h2><p>Son {orderItems.length} sipariş</p></div></div>
        <div className="donut" style={{ background: `conic-gradient(var(--green-dot) 0 ${successRate}%, var(--surface-3) ${successRate}%)` }}><div><strong>%{successRate}</strong><span>başarılı</span></div></div>
        <div className="status-list">
          <span><i className="dot dot--green" /> Teslim edildi <b>{statusCounts.delivered}</b></span>
          <span><i className="dot dot--blue" /> Devam ediyor <b>{statusCounts.active}</b></span>
          <span><i className="dot dot--amber" /> Kurye bekliyor <b>{statusCounts.waiting}</b></span>
          <span><i className="dot dot--red" /> Sonuçlanamadı <b>{statusCounts.failed}</b></span>
        </div>
      </article>
    </div>

    <article className="panel orders-panel">
      <div className="panel__heading"><div><h2>Son siparişler</h2><p>En yeni operasyon kayıtları</p></div><Link className="text-button" to="/orders">Tümünü gör</Link></div>
      {recentOrders.length > 0 ? <div className="table-wrap"><table><thead><tr><th>Sipariş</th><th>Müşteri</th>{auth.isPlatformAdmin && <th>İşletme</th>}<th>Kurye</th><th>Durum</th><th>Zaman</th></tr></thead><tbody>
        {recentOrders.map(order => <tr key={order.id}>
          <td><strong>#{order.id.slice(0, 8)}</strong></td>
          <td>{order.customerName}<small className="cell-sub">{order.deliveryAddress}</small></td>
          {auth.isPlatformAdmin && <td>{businessNames.get(order.businessId) ?? '—'}</td>}
          <td>{order.courierId ? courierNames.get(order.courierId) ?? 'Atanmış kurye' : 'Atanmadı'}</td>
          <td><span className={`pill pill--${pillTone(order.status)}`}>{statusLabels[order.status] ?? 'Bilinmiyor'}</span></td>
          <td>{relativeTime(order.createdAtUtc)}</td>
        </tr>)}
      </tbody></table></div> : <p className="empty-state">Henüz sipariş bulunmuyor.</p>}
    </article>
  </section>
}

function pillTone(status: number) {
  if (status === 6) return 'green'
  if (status === 7 || status === 8 || status === 9) return 'red'
  if (status === 2) return 'amber'
  return 'blue'
}

function relativeTime(value: string) {
  const elapsedMinutes = Math.max(0, Math.floor((Date.now() - Date.parse(value)) / 60_000))
  if (elapsedMinutes < 1) return 'Şimdi'
  if (elapsedMinutes < 60) return `${elapsedMinutes} dk önce`
  const elapsedHours = Math.floor(elapsedMinutes / 60)
  if (elapsedHours < 24) return `${elapsedHours} sa önce`
  return new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' }).format(new Date(value))
}
