import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, CheckCheck, Save, SlidersHorizontal } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { getJson, postJson, requestJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { Business } from '../businesses/BusinessesPage'
import { useAuth } from '../auth/AuthProvider'

export interface OperationalAlert {
  id: string
  businessId: string
  orderId?: string
  courierId?: string
  type: number
  severity: number
  status: number
  title: string
  message: string
  firstDetectedAtUtc: string
  lastChangedAtUtc: string
  acknowledgedAtUtc?: string
  resolvedAtUtc?: string
}

interface SlaSettings {
  businessId: string
  courierWaitingWarningMinutes: number
  courierWaitingCriticalMinutes: number
  pickupWarningMinutes: number
  pickupCriticalMinutes: number
  deliveryWarningMinutes: number
  deliveryCriticalMinutes: number
  locationStaleWarningMinutes: number
  locationStaleCriticalMinutes: number
}

export function AlertsPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [selectedBusinessId, setSelectedBusinessId] = useState(auth.user?.businessId ?? '')
  const [includeResolved, setIncludeResolved] = useState(false)
  const [showSettings, setShowSettings] = useState(false)
  const businesses = useQuery({ queryKey: ['businesses'], queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const businessId = auth.user?.businessId || selectedBusinessId || businesses.data?.items[0]?.id || ''
  const alerts = useQuery({
    queryKey: ['operational-alerts', businessId, includeResolved], enabled: !!businessId,
    queryFn: ({ signal }) => getJson<PagedResponse<OperationalAlert>>(`/api/v1/operations/alerts?businessId=${businessId}&includeResolved=${includeResolved}&pageSize=100`, signal),
    refetchInterval: 30_000,
  })
  const settings = useQuery({ queryKey: ['sla-settings', businessId], enabled: !!businessId, queryFn: ({ signal }) => getJson<SlaSettings>(`/api/v1/operations/sla-settings?businessId=${businessId}`, signal) })
  const [form, setForm] = useState<SlaSettings | null>(null)
  useEffect(() => { if (settings.data) setForm(settings.data) }, [settings.data])
  const acknowledge = useMutation({ mutationFn: (id: string) => postJson<OperationalAlert>(`/api/v1/operations/alerts/${id}/acknowledge`, {}), onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['operational-alerts'] }) })
  const saveSettings = useMutation({
    mutationFn: () => requestJson<SlaSettings>('/api/v1/operations/sla-settings', { method: 'PUT', body: JSON.stringify({ ...form, businessId }) }),
    onSuccess: data => { setForm(data); void queryClient.invalidateQueries({ queryKey: ['sla-settings', businessId] }) },
  })

  const activeAlerts = alerts.data?.items.filter(item => item.status !== 2) ?? []
  const criticalCount = activeAlerts.filter(item => item.severity === 2).length
  function submitSettings(event: FormEvent) { event.preventDefault(); saveSettings.mutate() }
  function setMinutes(key: keyof SlaSettings, value: string) { setForm(current => current ? { ...current, [key]: Number(value) } : current) }

  return <section>
    <div className="page-heading"><div><p className="eyebrow">OPERASYON ALARMLARI</p><h1>Bildirim merkezi</h1><p>SLA ihlallerini, geciken siparişleri ve kurye konum sorunlarını yönetin.</p></div><button className="primary-button" onClick={() => setShowSettings(value => !value)}><SlidersHorizontal size={17} /> SLA ayarları</button></div>
    <div className="alert-summary-grid"><article><strong>{activeAlerts.length}</strong><span>Açık uyarı</span></article><article className="alert-summary--critical"><strong>{criticalCount}</strong><span>Kritik</span></article><article><strong>{activeAlerts.filter(item => item.status === 1).length}</strong><span>İnceleniyor</span></article></div>

    <div className="alerts-toolbar">
      {auth.isPlatformAdmin && <label>İşletme<select value={businessId} onChange={event => setSelectedBusinessId(event.target.value)}>{businesses.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
      <label className="checkbox-label"><input type="checkbox" checked={includeResolved} onChange={event => setIncludeResolved(event.target.checked)} /> Çözülmüşleri göster</label>
    </div>

    {showSettings && form && <form className="panel sla-panel" onSubmit={submitSettings}>
      <div className="panel__heading"><div><h2>SLA eşikleri</h2><p>Uyarı ve kritik seviyeye geçiş süreleri, dakika</p></div></div>
      <div className="sla-grid">
        <SlaPair title="Kurye bekleme" warning={form.courierWaitingWarningMinutes} critical={form.courierWaitingCriticalMinutes} onWarning={value => setMinutes('courierWaitingWarningMinutes', value)} onCritical={value => setMinutes('courierWaitingCriticalMinutes', value)} />
        <SlaPair title="Teslim alma" warning={form.pickupWarningMinutes} critical={form.pickupCriticalMinutes} onWarning={value => setMinutes('pickupWarningMinutes', value)} onCritical={value => setMinutes('pickupCriticalMinutes', value)} />
        <SlaPair title="Teslimat" warning={form.deliveryWarningMinutes} critical={form.deliveryCriticalMinutes} onWarning={value => setMinutes('deliveryWarningMinutes', value)} onCritical={value => setMinutes('deliveryCriticalMinutes', value)} />
        <SlaPair title="Kurye konumu" warning={form.locationStaleWarningMinutes} critical={form.locationStaleCriticalMinutes} onWarning={value => setMinutes('locationStaleWarningMinutes', value)} onCritical={value => setMinutes('locationStaleCriticalMinutes', value)} />
      </div>
      <div className="sla-actions"><button className="primary-button" disabled={saveSettings.isPending}><Save size={16} /> Kaydet</button>{saveSettings.isSuccess && <span className="save-success">SLA ayarları kaydedildi.</span>}{saveSettings.error && <span className="form-error">{saveSettings.error.message}</span>}</div>
    </form>}

    <div className="alert-list">
      {alerts.data?.items.map(alert => <article className={`panel alert-card alert-card--${severityTone(alert.severity)}${alert.status === 2 ? ' alert-card--resolved' : ''}`} key={alert.id}>
        <span className="alert-card__icon"><AlertTriangle size={18} /></span><div className="alert-card__copy"><span className="alert-card__meta"><b>{severityLabel(alert.severity)}</b> · {typeLabel(alert.type)} · {relativeTime(alert.firstDetectedAtUtc)}</span><h2>{alert.title}</h2><p>{alert.message}</p><span className="alert-card__links">{alert.orderId && <Link to="/orders">Siparişi aç</Link>}{alert.status === 1 && <em>İnceleniyor</em>}{alert.status === 2 && <em>Otomatik çözüldü</em>}</span></div>
        {alert.type === 5 && <Link className="row-action" to="/integrations">Entegrasyonu aç</Link>}{alert.status === 0 && auth.hasPermission('dispatch.write') && <button className="row-action row-action--primary" onClick={() => acknowledge.mutate(alert.id)} disabled={acknowledge.isPending}><CheckCheck size={14} /> İnceliyorum</button>}
      </article>)}
      {alerts.isLoading && <p className="empty-state">Uyarılar yükleniyor…</p>}
      {!alerts.isLoading && alerts.data?.totalCount === 0 && <div className="panel alert-empty"><CheckCheck size={30} /><h2>Operasyon normal</h2><p>Açık SLA ihlali veya kurye konum uyarısı bulunmuyor.</p></div>}
      {alerts.error && <p className="form-error">{alerts.error.message}</p>}
    </div>
  </section>
}

function SlaPair({ title, warning, critical, onWarning, onCritical }: { title: string; warning: number; critical: number; onWarning(value: string): void; onCritical(value: string): void }) {
  return <fieldset><legend>{title}</legend><label>Uyarı<input type="number" min="1" max="1440" value={warning} onChange={event => onWarning(event.target.value)} /></label><label>Kritik<input type="number" min="2" max="1440" value={critical} onChange={event => onCritical(event.target.value)} /></label></fieldset>
}
function severityTone(severity: number) { return severity === 2 ? 'critical' : severity === 1 ? 'warning' : 'info' }
function severityLabel(severity: number) { return severity === 2 ? 'Kritik' : severity === 1 ? 'Uyarı' : 'Bilgi' }
function typeLabel(type: number) { return ['Kurye bekleme', 'Teslim alma', 'Teslimat', 'Kurye konumu', 'Kredi bakiyesi', 'Entegrasyon bağlantısı'][type] ?? 'Operasyon' }
function relativeTime(value: string) { const minutes = Math.max(0, Math.floor((Date.now() - Date.parse(value)) / 60_000)); return minutes < 1 ? 'şimdi' : minutes < 60 ? `${minutes} dk önce` : `${Math.floor(minutes / 60)} sa önce` }
