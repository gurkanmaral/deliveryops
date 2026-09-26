import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Save, Settings2 } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { getJson, requestJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { Business } from '../businesses/BusinessesPage'
import { useAuth } from '../auth/AuthProvider'

interface DispatchSettings {
  businessId: string
  autoConfirmOrders: boolean
  autoAssignCouriers: boolean
  allowCourierSelfClaim: boolean
  preferBranchCouriers: boolean
  maxActiveOrdersPerCourier: number
  requireFreshLocation: boolean
  locationFreshnessMinutes: number
  assignmentRadiusKm?: number
  preferDeliveryClusters: boolean
  deliveryClusterRadiusKm: number
  deliveryClusterMaxBearingDegrees: number
}

interface RoadRoutingStatus {
  isAvailable: boolean
  provider: string
  travelMode: string
  failClosed: boolean
}

const initialSettings: DispatchSettings = {
  businessId: '', autoConfirmOrders: false, autoAssignCouriers: false,
  allowCourierSelfClaim: true, preferBranchCouriers: true, maxActiveOrdersPerCourier: 2,
  requireFreshLocation: false, locationFreshnessMinutes: 5,
  preferDeliveryClusters: true, deliveryClusterRadiusKm: 2, deliveryClusterMaxBearingDegrees: 45,
}

export function DispatchSettingsPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const businesses = useQuery({
    queryKey: ['businesses'],
    queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal),
  })
  const [selectedBusinessId, setSelectedBusinessId] = useState(auth.user?.businessId ?? '')
  const businessId = auth.user?.businessId || selectedBusinessId || businesses.data?.items[0]?.id || ''
  const settingsQuery = useQuery({
    queryKey: ['dispatch-settings', businessId],
    enabled: !!businessId,
    queryFn: ({ signal }) => getJson<DispatchSettings>(`/api/v1/dispatch/settings?businessId=${businessId}`, signal),
  })
  const routingStatus = useQuery({
    queryKey: ['road-routing-status'],
    queryFn: ({ signal }) => getJson<RoadRoutingStatus>('/api/v1/dispatch/routing-status', signal),
  })
  const [form, setForm] = useState<DispatchSettings>(initialSettings)
  useEffect(() => { if (settingsQuery.data) setForm({ ...initialSettings, ...settingsQuery.data }) }, [settingsQuery.data])

  const save = useMutation({
    mutationFn: () => requestJson<DispatchSettings>('/api/v1/dispatch/settings', {
      method: 'PUT', body: JSON.stringify({ ...form, businessId, assignmentRadiusKm: form.assignmentRadiusKm || null }),
    }),
    onSuccess: data => {
      setForm(data)
      void queryClient.invalidateQueries({ queryKey: ['dispatch-settings', businessId] })
    },
  })

  function submit(event: FormEvent) { event.preventDefault(); save.mutate() }
  function toggle(key: keyof DispatchSettings) {
    setForm(current => ({ ...current, [key]: !current[key] }))
  }

  return <section>
    <div className="page-heading"><div><p className="eyebrow">SEVKİYAT OTOMASYONU</p><h1>Kurye atama ayarları</h1><p>Siparişlerin onaylanmasını, uygun kurye seçimini ve kapasite kurallarını işletme bazında yönetin.</p></div></div>

    {auth.isPlatformAdmin && <article className="panel settings-business-picker">
      <label>İşletme<select value={businessId} onChange={event => setSelectedBusinessId(event.target.value)}>{businesses.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    </article>}

    {settingsQuery.isLoading && <div className="api-notice">Atama ayarları yükleniyor…</div>}
    {settingsQuery.error && <p className="form-error form-error--panel">{settingsQuery.error.message}</p>}
    {!!businessId && !settingsQuery.isLoading && <form className="settings-grid" onSubmit={submit}>
      <article className="panel settings-card">
        <div className="panel__heading"><div><h2><Settings2 size={17} className="inline-icon" /> Otomasyon</h2><p>Sipariş sisteme geldiğinde uygulanacak akış</p></div></div>
        <SettingToggle checked={form.autoConfirmOrders} title="Siparişi otomatik onayla" description="Yeni siparişi doğrudan kurye bekleme kuyruğuna alır." onChange={() => toggle('autoConfirmOrders')} />
        <SettingToggle checked={form.autoAssignCouriers} title="Kuryeyi otomatik ata" description="Kuyruktaki sipariş için uygun kuryeyi arka planda seçer. Açıldığında otomatik onay da etkinleşir." onChange={() => setForm(current => ({ ...current, autoAssignCouriers: !current.autoAssignCouriers, autoConfirmOrders: !current.autoAssignCouriers || current.autoConfirmOrders }))} />
        <SettingToggle checked={form.allowCourierSelfClaim} title="Kurye paketi üstlenebilsin" description="Mobil uygulamadaki uygun paketler listesinden kendi seçimini yapabilir." onChange={() => toggle('allowCourierSelfClaim')} />
        <SettingToggle checked={form.preferBranchCouriers} title="Şube kuryesine öncelik ver" description="Ortak işletme kuryesinden önce siparişin şubesine bağlı kuryeyi seçer." onChange={() => toggle('preferBranchCouriers')} />
      </article>

      <article className="panel settings-card">
        <div className="panel__heading"><div><h2>Kapasite ve konum</h2><p>Atama motorunun aday filtreleri</p></div></div>
        <div className="settings-fields">
          <label>Kurye başına aktif paket<input type="number" min="1" max="20" value={form.maxActiveOrdersPerCourier} onChange={event => setForm(current => ({ ...current, maxActiveOrdersPerCourier: Number(event.target.value) }))} /></label>
          <SettingToggle checked={form.requireFreshLocation} title="Güncel konum zorunlu" description="Eski veya konumu olmayan kuryeleri aday listesinden çıkarır." onChange={() => toggle('requireFreshLocation')} compact />
          <label>Konum geçerlilik süresi (dk)<input type="number" min="1" max="120" value={form.locationFreshnessMinutes} onChange={event => setForm(current => ({ ...current, locationFreshnessMinutes: Number(event.target.value) }))} /></label>
          <label>Azami atama mesafesi (km)<input type="number" min="0.1" max="200" step="0.1" placeholder="Sınırsız" value={form.assignmentRadiusKm ?? ''} onChange={event => setForm(current => ({ ...current, assignmentRadiusKm: event.target.value ? Number(event.target.value) : undefined }))} /></label>
          <p className="settings-hint">Alım mesafesi kuryenin son konumu ile siparişin alınacağı şube arasında hesaplanır.</p>
        </div>
      </article>

      <article className="panel settings-card settings-card--route">
        <div className="panel__heading"><div><h2>Rota gruplama</h2><p>Aynı yöne giden paketleri kontrollü biçimde aynı kuryede toplayın</p></div><div className="route-rule"><strong>{form.deliveryClusterRadiusKm.toFixed(1)} km</strong><span>en fazla {form.deliveryClusterMaxBearingDegrees}° yön farkı</span></div></div>
        <div className="settings-fields">
          <div className={`routing-provider-status ${routingStatus.data?.isAvailable ? 'routing-provider-status--ready' : 'routing-provider-status--offline'}`}><strong>{routingStatus.data?.isAvailable ? 'Gerçek yol doğrulaması yapılandırıldı' : 'Gerçek yol doğrulaması kapalı'}</strong><span>{routingStatus.data?.isAvailable ? `${routingStatus.data.provider} · motosiklet rotası · servis hatasında güvenli kapatma` : 'API anahtarı tanımlanana kadar sistem paketleri otomatik rota grubuna almaz.'}</span></div>
          <SettingToggle checked={form.preferDeliveryClusters} title="Aynı rota paketlerini grupla" description="Yalnızca doğrulanmış koordinatı olan ve aynı çıkış yönündeki teslimatları gruplar." onChange={() => toggle('preferDeliveryClusters')} compact />
          <div className="route-presets" aria-label="Rota gruplama hazır ayarları">
            <button type="button" onClick={() => setForm(current => ({ ...current, deliveryClusterRadiusKm: 1, deliveryClusterMaxBearingDegrees: 30 }))}>Sıkı · 1 km / 30°</button>
            <button type="button" onClick={() => setForm(current => ({ ...current, deliveryClusterRadiusKm: 2, deliveryClusterMaxBearingDegrees: 45 }))}>Dengeli · 2 km / 45°</button>
            <button type="button" onClick={() => setForm(current => ({ ...current, deliveryClusterRadiusKm: 4, deliveryClusterMaxBearingDegrees: 60 }))}>Geniş · 4 km / 60°</button>
          </div>
          <label>Teslimat yakınlık yarıçapı (km)<input type="number" min="0.1" max="25" step="0.1" value={form.deliveryClusterRadiusKm} disabled={!form.preferDeliveryClusters} onChange={event => setForm(current => ({ ...current, deliveryClusterRadiusKm: Number(event.target.value) }))} /></label>
          <label>Azami yön farkı (derece)<input type="number" min="5" max="180" step="1" value={form.deliveryClusterMaxBearingDegrees} disabled={!form.preferDeliveryClusters} onChange={event => setForm(current => ({ ...current, deliveryClusterMaxBearingDegrees: Number(event.target.value) }))} /></label>
          <p className="settings-hint">Kuş uçuşu mesafe yalnızca ön elemedir. Nihai eşleşme motosikletin gerçekten kullanacağı yol mesafesiyle doğrulanır; servis hatasında veya yaklaşık koordinatta otomatik gruplama yapılmaz.</p>
        </div>
      </article>

      <div className="settings-actions">
        <button className="primary-button" disabled={save.isPending}><Save size={17} /> {save.isPending ? 'Kaydediliyor…' : 'Ayarları kaydet'}</button>
        {save.isSuccess && <span className="save-success">Ayarlar kaydedildi.</span>}
        {save.error && <span className="form-error">{save.error.message}</span>}
      </div>
    </form>}
  </section>
}

function SettingToggle({ checked, title, description, onChange, compact = false }: { checked: boolean; title: string; description: string; onChange(): void; compact?: boolean }) {
  return <button type="button" className={`setting-toggle${compact ? ' setting-toggle--compact' : ''}`} onClick={onChange} aria-pressed={checked}>
    <span><strong>{title}</strong><small>{description}</small></span><i className={checked ? 'switch switch--on' : 'switch'}><b /></i>
  </button>
}
