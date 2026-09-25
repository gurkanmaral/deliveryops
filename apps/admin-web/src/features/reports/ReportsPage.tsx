import { useQuery } from '@tanstack/react-query'
import { Download, FileSpreadsheet, Gauge, PackageCheck, PackageOpen, Timer, TrendingUp, WalletCards } from 'lucide-react'
import { useState } from 'react'
import { downloadFile, getJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { Business } from '../businesses/BusinessesPage'
import { useAuth } from '../auth/AuthProvider'

interface ReportSummary {
  totalOrders: number; deliveredOrders: number; cancelledOrders: number; failedOrders: number; openOrders: number
  totalOrderValue: number; deliveredValue: number; deliverySuccessRate: number
  averageAssignmentMinutes?: number; averagePickupMinutes?: number; averageDeliveryMinutes?: number; averageTotalMinutes?: number
}
interface DailyMetric { date: string; totalOrders: number; deliveredOrders: number; cancelledOrders: number; failedOrders: number; openOrders: number; totalOrderValue: number; deliveredValue: number }
interface CourierMetric { courierId: string; courierName: string; assignedOrders: number; deliveredOrders: number; failedOrders: number; openOrders: number; deliverySuccessRate: number; averageDeliveryMinutes?: number; averageTotalMinutes?: number }
interface BranchMetric { branchId: string; businessId: string; businessName: string; branchName: string; totalOrders: number; deliveredOrders: number; cancelledOrders: number; failedOrders: number; openOrders: number; deliverySuccessRate: number; averageTotalMinutes?: number; totalOrderValue: number; deliveredValue: number }
interface SourceMetric { source: number; totalOrders: number; deliveredOrders: number; failedOrders: number; deliverySuccessRate: number; totalOrderValue: number }
interface OperationsReport { from: string; to: string; timeZone: string; businessId?: string; summary: ReportSummary; daily: DailyMetric[]; couriers: CourierMetric[]; branches: BranchMetric[]; sources: SourceMetric[] }

const sourceLabels = ['Telefon', 'Admin paneli', 'İşletme paneli', 'Yemeksepeti', 'Getir', 'POS', 'Diğer', 'Trendyol']
const numberFormat = new Intl.NumberFormat('tr-TR')
const currencyFormat = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', maximumFractionDigits: 0 })
const dayFormat = new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short' })

export function ReportsPage() {
  const auth = useAuth()
  const [defaults] = useState(defaultDateRange)
  const [from, setFrom] = useState(defaults.from)
  const [to, setTo] = useState(defaults.to)
  const [businessId, setBusinessId] = useState(auth.user?.businessId ?? '')
  const [downloading, setDownloading] = useState<'xlsx' | 'csv' | null>(null)
  const businesses = useQuery({ queryKey: ['businesses'], enabled: auth.isPlatformAdmin, queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const queryString = new URLSearchParams({ from, to, ...(businessId ? { businessId } : {}) }).toString()
  const report = useQuery({
    queryKey: ['operations-report', from, to, businessId],
    enabled: Boolean(from && to && from <= to),
    queryFn: ({ signal }) => getJson<OperationsReport>(`/api/v1/reports/operations?${queryString}`, signal),
  })

  const maxDaily = Math.max(1, ...(report.data?.daily.map(item => item.totalOrders) ?? [1]))
  const maxSource = Math.max(1, ...(report.data?.sources.map(item => item.totalOrders) ?? [1]))
  const summary = report.data?.summary
  const metrics = [
    { label: 'Toplam sipariş', value: summary ? numberFormat.format(summary.totalOrders) : '—', detail: 'Seçili dönemde oluşturulan', icon: PackageOpen },
    { label: 'Teslim edilen', value: summary ? numberFormat.format(summary.deliveredOrders) : '—', detail: `${summary ? percent(summary.deliverySuccessRate) : '—'} başarı oranı`, icon: PackageCheck },
    { label: 'Ort. toplam süre', value: duration(summary?.averageTotalMinutes), detail: 'Siparişten teslimata', icon: Timer },
    { label: 'Ort. atama', value: duration(summary?.averageAssignmentMinutes), detail: 'Siparişten kurye atamasına', icon: Gauge },
    { label: 'Teslim edilen tutar', value: summary ? currencyFormat.format(summary.deliveredValue) : '—', detail: 'Başarıyla tamamlanan siparişler', icon: WalletCards },
    { label: 'Açık sipariş', value: summary ? numberFormat.format(summary.openOrders) : '—', detail: `${summary?.failedOrders ?? 0} başarısız / iade`, icon: TrendingUp },
  ]

  async function exportReport(format: 'xlsx' | 'csv') {
    setDownloading(format)
    try { await downloadFile(`/api/v1/reports/operations/export?${queryString}&format=${format}`, `operasyon-raporu.${format}`) }
    finally { setDownloading(null) }
  }

  return <section>
    <div className="page-heading"><div><p className="eyebrow">PERFORMANS VE ANALİZ</p><h1>Operasyon raporları</h1><p>Sipariş hacmini, teslimat sürelerini ve kurye performansını karşılaştırın.</p></div><div className="report-export-actions"><button className="secondary-button" disabled={!report.data || downloading !== null} onClick={() => void exportReport('csv')}><Download size={16} /> CSV</button><button className="primary-button" disabled={!report.data || downloading !== null} onClick={() => void exportReport('xlsx')}><FileSpreadsheet size={16} /> {downloading === 'xlsx' ? 'Hazırlanıyor…' : 'Excel indir'}</button></div></div>

    <div className="panel report-filters">
      <label>Başlangıç<input type="date" value={from} max={to} onChange={event => setFrom(event.target.value)} /></label>
      <label>Bitiş<input type="date" value={to} min={from} onChange={event => setTo(event.target.value)} /></label>
      {auth.isPlatformAdmin && <label>İşletme<select value={businessId} onChange={event => setBusinessId(event.target.value)}><option value="">Tüm işletmeler</option>{businesses.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
      <span>Raporlar sipariş oluşturma tarihine göre ve Türkiye saat diliminde hesaplanır.</span>
    </div>

    {report.error && <p className="form-error report-error">{report.error.message}</p>}
    <div className="report-metric-grid">{metrics.map(({ label, value, detail, icon: Icon }) => <article className="metric-card" key={label}><div className="metric-card__top"><span>{label}</span><span className="metric-card__icon"><Icon size={18} /></span></div><strong>{report.isLoading ? '—' : value}</strong><small>{detail}</small></article>)}</div>

    <div className="report-chart-grid">
      <article className="panel report-chart-panel"><div className="panel__heading"><div><h2>Günlük sipariş hacmi</h2><p>Toplam ve teslim edilen siparişler</p></div><span className="chart-legend"><i /> Toplam <i /> Teslim</span></div>
        <div className="daily-chart">{report.data?.daily.map(item => <div className="daily-chart__column" key={item.date} title={`${item.date}: ${item.totalOrders} sipariş, ${item.deliveredOrders} teslim`}><div className="daily-chart__value">{item.totalOrders || ''}</div><div className="daily-chart__track"><i style={{ height: `${Math.max(2, item.totalOrders / maxDaily * 100)}%` }}><b style={{ height: `${item.totalOrders ? item.deliveredOrders / item.totalOrders * 100 : 0}%` }} /></i></div><span>{dayFormat.format(new Date(`${item.date}T12:00:00`))}</span></div>)}</div>
      </article>
      <article className="panel source-panel"><div className="panel__heading"><div><h2>Sipariş kaynakları</h2><p>Kanal bazında hacim ve başarı</p></div></div><div className="source-list">{report.data?.sources.map(item => <div key={item.source}><span><strong>{sourceLabels[item.source] ?? 'Diğer'}</strong><small>{numberFormat.format(item.totalOrders)} sipariş · {percent(item.deliverySuccessRate)}</small></span><i><b style={{ width: `${item.totalOrders / maxSource * 100}%` }} /></i></div>)}{report.data?.sources.length === 0 && <p className="empty-state">Bu dönemde sipariş yok.</p>}</div></article>
    </div>

    <article className="panel report-table-panel"><div className="panel__heading"><div><h2>Kurye performansı</h2><p>Atanan paketler ve teslimat süreleri</p></div></div><div className="table-wrap"><table><thead><tr><th>Kurye</th><th>Atanan</th><th>Teslim</th><th>Başarısız</th><th>Açık</th><th>Başarı</th><th>Ort. teslimat</th><th>Ort. toplam</th></tr></thead><tbody>{report.data?.couriers.map(item => <tr key={item.courierId}><td><strong>{item.courierName}</strong></td><td>{item.assignedOrders}</td><td>{item.deliveredOrders}</td><td>{item.failedOrders}</td><td>{item.openOrders}</td><td><span className={`pill pill--${item.deliverySuccessRate >= .9 ? 'green' : item.deliverySuccessRate >= .7 ? 'amber' : 'red'}`}>{percent(item.deliverySuccessRate)}</span></td><td>{duration(item.averageDeliveryMinutes)}</td><td>{duration(item.averageTotalMinutes)}</td></tr>)}</tbody></table>{report.data?.couriers.length === 0 && <p className="empty-state">Kurye performans verisi bulunmuyor.</p>}</div></article>

    <article className="panel report-table-panel"><div className="panel__heading"><div><h2>Şube performansı</h2><p>Hacim, başarı oranı ve teslim edilen tutar</p></div></div><div className="table-wrap"><table><thead><tr>{auth.isPlatformAdmin && <th>İşletme</th>}<th>Şube</th><th>Toplam</th><th>Teslim</th><th>İptal / Başarısız</th><th>Başarı</th><th>Ort. süre</th><th>Teslim edilen tutar</th></tr></thead><tbody>{report.data?.branches.map(item => <tr key={item.branchId}>{auth.isPlatformAdmin && <td>{item.businessName}</td>}<td><strong>{item.branchName}</strong></td><td>{item.totalOrders}</td><td>{item.deliveredOrders}</td><td>{item.cancelledOrders + item.failedOrders}</td><td>{percent(item.deliverySuccessRate)}</td><td>{duration(item.averageTotalMinutes)}</td><td>{currencyFormat.format(item.deliveredValue)}</td></tr>)}</tbody></table>{report.data?.branches.length === 0 && <p className="empty-state">Şube performans verisi bulunmuyor.</p>}</div></article>
  </section>
}

function defaultDateRange() {
  const to = new Date()
  const from = new Date(to)
  from.setDate(from.getDate() - 29)
  return { from: localDate(from), to: localDate(to) }
}
function localDate(date: Date) { return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}` }
function percent(value: number) { return new Intl.NumberFormat('tr-TR', { style: 'percent', maximumFractionDigits: 1 }).format(value) }
function duration(value?: number) { return value === undefined || value === null ? '—' : value < 60 ? `${Math.round(value)} dk` : `${Math.floor(value / 60)} sa ${Math.round(value % 60)} dk` }
