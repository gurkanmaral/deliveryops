import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Activity, CircleCheck, Clock3, Copy, KeyRound, Plus, RefreshCw, RotateCcw, TriangleAlert, Wifi } from 'lucide-react'
import { useMemo, useState, type FormEvent } from 'react'
import { getIntegrationsApiUrl, getJson, postJson, requestJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { Branch, Business } from '../businesses/BusinessesPage'
import { useAuth } from '../auth/AuthProvider'

interface IntegrationConnection {
  id: string
  businessId: string
  branchId: string
  provider: number
  name: string
  authMode: number
  adapterVersion: string
  isActive: boolean
  createdAtUtc: string
  webhookPath: string
  secret: string | null
  credentialsConfigured: boolean
  providerAccountId: string | null
  providerEnvironment: number
  lastHealthCheckAtUtc: string | null
  lastHealthCheckSucceeded: boolean | null
  lastHealthCheckMessage: string | null
  lastTokenExpiresAtUtc: string | null
  consecutiveHealthCheckFailures: number
  lastAutomaticHealthCheckAtUtc: string | null
  previousSecretValidUntilUtc: string | null
}

interface IntegrationConnectionTestResult {
  success: boolean
  environment: number
  checkedAtUtc: string
  tokenExpiresAtUtc: string | null
  message: string
}

interface IntegrationMonitoring {
  fromUtc: string
  toUtc: string
  days: number
  connectionCount: number
  activeConnectionCount: number
  unhealthyConnectionCount: number
  inboundTotal: number
  inboundCompleted: number
  outboundTotal: number
  outboundCompleted: number
  retryAttempts: number
  deadLetters: number
  successRatePercent: number | null
  availabilityPercent: number | null
  averageProcessingMs: number | null
  lastSuccessfulCommunicationAtUtc: string | null
  providers: ProviderMetric[]
  connections: ConnectionMetric[]
  daily: DailyMetric[]
}

interface ProviderMetric {
  provider: number
  connectionCount: number
  activeConnectionCount: number
  inboundTotal: number
  inboundCompleted: number
  outboundTotal: number
  outboundCompleted: number
  retryAttempts: number
  deadLetters: number
  successRatePercent: number | null
  availabilityPercent: number | null
}

interface ConnectionMetric {
  connectionId: string
  businessId: string
  name: string
  provider: number
  isActive: boolean
  lastHealthCheckSucceeded: boolean | null
  lastHealthCheckAtUtc: string | null
  inboundTotal: number
  inboundCompleted: number
  outboundTotal: number
  outboundCompleted: number
  retryAttempts: number
  deadLetters: number
  successRatePercent: number | null
  healthChecks: number
  successfulHealthChecks: number
  availabilityPercent: number | null
  averageProcessingMs: number | null
  lastSuccessfulCommunicationAtUtc: string | null
}

interface DailyMetric {
  date: string
  inboundTotal: number
  inboundCompleted: number
  outboundTotal: number
  outboundCompleted: number
}

interface InboundEvent {
  id: string
  connectionId: string
  externalEventId: string
  externalOrderId: string
  eventType: string
  adapterVersion: string
  status: number
  attempts: number
  coreOrderId: string | null
  lastError: string | null
  receivedAtUtc: string
  nextAttemptAtUtc: string | null
}

interface OutboundEvent {
  id: string
  connectionId: string
  coreOrderId: string
  externalOrderId: string
  providerStatus: string
  status: number
  attempts: number
  lastError: string | null
  createdAtUtc: string
  nextAttemptAtUtc: string | null
}

const providerLabels = ['Yemeksepeti', 'Getir', 'POS', 'Trendyol']

export function IntegrationsPage() {
  const auth = useAuth()
  const client = useQueryClient()
  const businesses = useQuery({ queryKey: ['businesses'], queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const [businessId, setBusinessId] = useState('')
  const activeBusinessId = auth.user?.businessId || businessId || businesses.data?.items[0]?.id || ''
  const canWrite = auth.hasPermission('integrations.write')
  const branches = useQuery({ queryKey: ['branches', activeBusinessId], enabled: !!activeBusinessId, queryFn: ({ signal }) => getJson<PagedResponse<Branch>>(`/api/v1/branches?businessId=${activeBusinessId}&pageSize=100`, signal) })
  const connections = useQuery({
    queryKey: ['integrations', activeBusinessId], enabled: !!activeBusinessId,
    queryFn: ({ signal }) => getJson<PagedResponse<IntegrationConnection>>('/api/v1/integrations?pageSize=100', signal, 'integrations'),
  })
  const events = useQuery({
    queryKey: ['integration-events', activeBusinessId], enabled: !!activeBusinessId,
    queryFn: ({ signal }) => getJson<PagedResponse<InboundEvent>>(`/api/v1/integrations/events?businessId=${activeBusinessId}&pageSize=50`, signal, 'integrations'),
    refetchInterval: 15_000,
  })
  const outboundEvents = useQuery({
    queryKey: ['integration-outbound-events', activeBusinessId], enabled: !!activeBusinessId,
    queryFn: ({ signal }) => getJson<PagedResponse<OutboundEvent>>(`/api/v1/integrations/outbound-events?businessId=${activeBusinessId}&pageSize=50`, signal, 'integrations'),
    refetchInterval: 15_000,
  })
  const [monitorDays, setMonitorDays] = useState('7')
  const monitoring = useQuery({
    queryKey: ['integration-monitoring', activeBusinessId, monitorDays], enabled: !!activeBusinessId,
    queryFn: ({ signal }) => getJson<IntegrationMonitoring>(`/api/v1/integrations/monitoring?businessId=${activeBusinessId}&days=${monitorDays}`, signal, 'integrations'),
    refetchInterval: 30_000,
  })
  const visibleConnections = useMemo(() => connections.data?.items.filter(item => item.businessId === activeBusinessId) ?? [], [activeBusinessId, connections.data])
  const [branchId, setBranchId] = useState('')
  const [provider, setProvider] = useState('2')
  const [authMode, setAuthMode] = useState('0')
  const [name, setName] = useState('')
  const [issuedSecret, setIssuedSecret] = useState<IntegrationConnection | null>(null)
  const [copied, setCopied] = useState('')
  const [credentialConnection, setCredentialConnection] = useState<IntegrationConnection | null>(null)
  const [clientId, setClientId] = useState('')
  const [clientSecret, setClientSecret] = useState('')
  const [chainId, setChainId] = useState('')
  const [providerEnvironment, setProviderEnvironment] = useState('0')
  const refresh = () => void client.invalidateQueries({ queryKey: ['integrations'] })
  const create = useMutation({
    mutationFn: () => postJson<IntegrationConnection>('/api/v1/integrations', {
      businessId: activeBusinessId, branchId, provider: Number(provider), name, authMode: Number(authMode),
      adapterVersion: provider === '0' ? 'yemeksepeti-partner-v2' : provider === '1' ? 'getir-food-v1' : provider === '3' ? 'trendyol-webhook-v1' : 'canonical-v1',
    }, 'integrations'),
    onSuccess: connection => { setName(''); setIssuedSecret(connection); refresh() },
  })
  const toggle = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => requestJson<IntegrationConnection>(`/api/v1/integrations/${id}/active`, { api: 'integrations', method: 'PATCH', body: JSON.stringify({ isActive }) }),
    onSuccess: refresh,
  })
  const rotate = useMutation({
    mutationFn: (id: string) => postJson<IntegrationConnection>(`/api/v1/integrations/${id}/rotate-secret`, {}, 'integrations'),
    onSuccess: connection => { setIssuedSecret(connection); refresh() },
  })
  const retry = useMutation({
    mutationFn: (id: string) => postJson<InboundEvent>(`/api/v1/integrations/events/${id}/retry`, {}, 'integrations'),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['integration-events'] }),
  })
  const retryOutbound = useMutation({
    mutationFn: (id: string) => postJson<OutboundEvent>(`/api/v1/integrations/outbound-events/${id}/retry`, {}, 'integrations'),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['integration-outbound-events'] }),
  })
  const configureYemeksepeti = useMutation({
    mutationFn: () => requestJson<IntegrationConnection>(`/api/v1/integrations/${credentialConnection!.id}/yemeksepeti-credentials`, { api: 'integrations', method: 'PUT', body: JSON.stringify({ clientId, clientSecret, chainId, environment: Number(providerEnvironment) }) }),
    onSuccess: () => { setCredentialConnection(null); setClientId(''); setClientSecret(''); setChainId(''); refresh() },
  })
  const testConnection = useMutation({
    mutationFn: (id: string) => postJson<IntegrationConnectionTestResult>(`/api/v1/integrations/${id}/test`, {}, 'integrations'),
    onSuccess: refresh,
  })

  function submit(event: FormEvent) { event.preventDefault(); create.mutate() }
  async function copy(label: string, value: string) {
    await navigator.clipboard.writeText(value)
    setCopied(label)
    window.setTimeout(() => setCopied(''), 1600)
  }

  const webhookUrl = issuedSecret ? `${getIntegrationsApiUrl()}${issuedSecret.webhookPath}` : ''
  const maxDailyEvents = Math.max(1, ...(monitoring.data?.daily.map(day => day.inboundTotal + day.outboundTotal) ?? [1]))
  const monitoringMetrics = [
    { label: 'Toplam trafik', value: (monitoring.data?.inboundTotal ?? 0) + (monitoring.data?.outboundTotal ?? 0), detail: `${monitoring.data?.inboundTotal ?? 0} gelen · ${monitoring.data?.outboundTotal ?? 0} giden`, icon: Activity },
    { label: 'Başarı oranı', value: formatPercent(monitoring.data?.successRatePercent), detail: 'Tamamlanan entegrasyon olayları', icon: CircleCheck },
    { label: 'Kullanılabilirlik', value: formatPercent(monitoring.data?.availabilityPercent), detail: `${monitoring.data?.activeConnectionCount ?? 0} aktif bağlantı`, icon: Wifi },
    { label: 'Ortalama süre', value: formatDuration(monitoring.data?.averageProcessingMs), detail: 'Uçtan uca işleme süresi', icon: Clock3 },
    { label: 'Retry', value: monitoring.data?.retryAttempts ?? 0, detail: 'Yeniden deneme adedi', icon: RotateCcw },
    { label: 'Dead letter', value: monitoring.data?.deadLetters ?? 0, detail: `${monitoring.data?.unhealthyConnectionCount ?? 0} sağlıksız bağlantı`, icon: TriangleAlert },
  ]

  return <section>
    <div className="page-heading"><div><p className="eyebrow">KANAL YÖNETİMİ</p><h1>Entegrasyonlar</h1><p>POS ve pazar yeri sipariş girişlerini işletme şubelerine bağlayın.</p></div></div>

    <article className="panel report-filters integration-monitoring-toolbar">
      <label>İzleme dönemi<select value={monitorDays} onChange={event => setMonitorDays(event.target.value)}><option value="1">Son 24 saat</option><option value="7">Son 7 gün</option><option value="30">Son 30 gün</option></select></label>
      <span>{monitoring.data?.lastSuccessfulCommunicationAtUtc ? `Son başarılı iletişim: ${new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(monitoring.data.lastSuccessfulCommunicationAtUtc))}` : 'Henüz başarılı iletişim yok'}</span>
    </article>
    <div className="report-metric-grid integration-monitoring-metrics">{monitoringMetrics.map(({ label, value, detail, icon: Icon }) => <article className="metric-card" key={label}><div className="metric-card__top"><span>{label}</span><span className="metric-card__icon"><Icon size={18} /></span></div><strong>{monitoring.isLoading ? '—' : value}</strong><small>{detail}</small></article>)}</div>
    <div className="report-chart-grid integration-monitoring-grid">
      <article className="panel report-chart-panel"><div className="panel__heading"><div><h2>Entegrasyon trafiği</h2><p>Gelen ve dış sisteme gönderilen olaylar</p></div><span className="chart-legend"><i /> Toplam <i /> Başarılı</span></div><div className="daily-chart">{monitoring.data?.daily.map(day => { const total = day.inboundTotal + day.outboundTotal; const completed = day.inboundCompleted + day.outboundCompleted; return <div className="daily-chart__column" key={day.date}><b className="daily-chart__value">{total}</b><span className="daily-chart__track"><i style={{ height: `${Math.max(total ? 4 : 0, total / maxDailyEvents * 100)}%` }}><b style={{ height: `${total ? completed / total * 100 : 0}%` }} /></i></span><span>{new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: '2-digit' }).format(new Date(day.date))}</span></div> })}</div></article>
      <article className="panel source-panel"><div className="panel__heading"><div><h2>Sağlayıcılar</h2><p>Kanal bazında başarı</p></div></div><div className="source-list">{monitoring.data?.providers.map(item => <div key={item.provider}><span><strong>{providerLabels[item.provider] ?? 'Diğer'}</strong><small>{item.inboundTotal + item.outboundTotal} olay · {formatPercent(item.successRatePercent)}</small></span><i><b style={{ width: `${item.successRatePercent ?? 0}%` }} /></i></div>)}{monitoring.data?.providers.length === 0 && <p className="empty-state">Bu dönemde sağlayıcı trafiği yok.</p>}</div></article>
    </div>
    <article className="panel orders-panel integration-quality-panel"><div className="panel__heading"><div><h2>Bağlantı kalitesi</h2><p>Uptime, gecikme ve hata dağılımı</p></div></div><div className="table-wrap"><table><thead><tr><th>Bağlantı</th><th>Sağlayıcı</th><th>Trafik</th><th>Başarı</th><th>Uptime</th><th>Ort. süre</th><th>Retry / dead</th><th>Son başarı</th></tr></thead><tbody>{monitoring.data?.connections.map(item => <tr key={item.connectionId}><td><strong>{item.name}</strong><small className="cell-sub">{item.isActive ? 'Aktif' : 'Pasif'}</small></td><td>{providerLabels[item.provider] ?? 'Diğer'}</td><td>{item.inboundTotal + item.outboundTotal}<small className="cell-sub">{item.inboundTotal} gelen · {item.outboundTotal} giden</small></td><td>{formatPercent(item.successRatePercent)}</td><td>{formatPercent(item.availabilityPercent)}<small className="cell-sub">{item.successfulHealthChecks}/{item.healthChecks} kontrol</small></td><td>{formatDuration(item.averageProcessingMs)}</td><td>{item.retryAttempts} / {item.deadLetters}</td><td>{item.lastSuccessfulCommunicationAtUtc ? new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(item.lastSuccessfulCommunicationAtUtc)) : '—'}</td></tr>)}</tbody></table></div>{monitoring.data?.connections.length === 0 && <p className="empty-state">İzlenecek bağlantı bulunmuyor.</p>}{monitoring.error && <p className="form-error form-error--panel">{monitoring.error.message}</p>}</article>

    {issuedSecret?.secret && <article className="secret-card">
      <div><KeyRound size={21} /><div><strong>Bağlantı anahtarı yalnızca şimdi gösteriliyor</strong><p>Webhook adresi ve anahtarı güvenli şekilde POS veya sağlayıcı yapılandırmasına kaydedin.</p></div></div>
      <label>Webhook URL<span><code>{webhookUrl}</code><button onClick={() => void copy('url', webhookUrl)}><Copy size={15} /> {copied === 'url' ? 'Kopyalandı' : 'Kopyala'}</button></span></label>
      <label>{issuedSecret.authMode === 1 ? 'HMAC signing secret' : issuedSecret.authMode === 2 ? 'Authorization secret' : 'X-DeliveryOps-Key'}<span><code>{issuedSecret.secret}</code><button onClick={() => void copy('secret', issuedSecret.secret!)}><Copy size={15} /> {copied === 'secret' ? 'Kopyalandı' : 'Kopyala'}</button></span></label>
      {issuedSecret.authMode === 1 && <p>İsteklerde Unix saniyesini <code>X-DeliveryOps-Timestamp</code>, <code>HMAC-SHA256(secret, timestamp + "." + rawBody)</code> sonucunu <code>X-DeliveryOps-Signature</code> başlığıyla gönderin.</p>}
      {issuedSecret.authMode === 2 && <p>Bu değeri Yemeksepeti Partner Portal webhook ayarındaki secret alanına girin. Gelen <code>Authorization</code> başlığı sabit zamanlı karşılaştırmayla doğrulanır.</p>}
      {issuedSecret.previousSecretValidUntilUtc && <p>Önceki anahtar <strong>{new Date(issuedSecret.previousSecretValidUntilUtc).toLocaleString('tr-TR')}</strong> tarihine kadar geçerlidir. Sağlayıcı ayarını bu süre dolmadan güncelleyin.</p>}
      <button className="text-button secret-card__close" onClick={() => setIssuedSecret(null)}>Anahtarı gizle</button>
    </article>}

    {canWrite && credentialConnection && <article className="panel section-panel">
      <div className="panel__heading"><div><h2>Yemeksepeti API erişimi</h2><p>{credentialConnection.name} için OAuth2 client-credentials ayarları</p></div><button className="text-button" onClick={() => setCredentialConnection(null)}>Kapat</button></div>
      <form className="inline-form" onSubmit={event => { event.preventDefault(); configureYemeksepeti.mutate() }}>
        <label>Ortam<select value={providerEnvironment} onChange={event => setProviderEnvironment(event.target.value)}><option value="0">Sandbox</option><option value="1">Production</option></select></label>
        <label>Chain ID<input value={chainId} onChange={event => setChainId(event.target.value)} required /></label>
        <label>Client ID<input value={clientId} onChange={event => setClientId(event.target.value)} autoComplete="off" required /></label>
        <label>Client secret<input type="password" value={clientSecret} onChange={event => setClientSecret(event.target.value)} autoComplete="new-password" required /></label>
        <button className="primary-button" disabled={configureYemeksepeti.isPending}>Güvenli kaydet</button>
      </form>
      <p className="cell-sub">Client bilgileri şifreli saklanır ve API cevaplarında tekrar gösterilmez.</p>
      {configureYemeksepeti.error && <p className="form-error form-error--panel">{configureYemeksepeti.error.message}</p>}
    </article>}

    {canWrite && <article className="panel section-panel">
      <div className="panel__heading"><div><h2>Yeni bağlantı</h2><p>Her şube ve sipariş kanalı için ayrı anahtar oluşturun</p></div></div>
      <form className="inline-form" onSubmit={submit}>
        {auth.isPlatformAdmin && <label>İşletme<select value={activeBusinessId} onChange={event => { setBusinessId(event.target.value); setBranchId('') }} required>{businesses.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        <label>Şube<select value={branchId} onChange={event => setBranchId(event.target.value)} required><option value="">Şube seçin</option>{branches.data?.items.filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Sağlayıcı<select value={provider} onChange={event => { setProvider(event.target.value); setAuthMode(event.target.value === '0' ? '2' : '0') }}>{providerLabels.map((label, index) => <option key={label} value={index}>{label}</option>)}</select></label>
        <label>Webhook güvenliği<select value={authMode} onChange={event => setAuthMode(event.target.value)}><option value="2">Authorization secret</option><option value="1">HMAC-SHA256</option><option value="0">API anahtarı</option></select></label>
        <label className="inline-form__wide">Bağlantı adı<input value={name} onChange={event => setName(event.target.value)} placeholder="Kadıköy kasa POS" required /></label>
        <button className="primary-button" disabled={!activeBusinessId || !branchId || create.isPending}><Plus size={17} /> Bağlantı oluştur</button>
      </form>
      {create.error && <p className="form-error form-error--panel">{create.error.message}</p>}
    </article>}

    <article className="panel orders-panel">
      <div className="panel__heading"><div><h2>Bağlantılar</h2><p>{visibleConnections.length} kayıt</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Bağlantı</th><th>Sağlayıcı</th><th>Şube</th><th>Güvenlik / adaptör</th><th>Durum</th><th>Webhook</th><th /></tr></thead><tbody>
        {visibleConnections.map(connection => <tr key={connection.id}>
          <td><strong>{connection.name}</strong></td><td>{providerLabels[connection.provider] ?? 'Diğer'}</td>
          <td>{branches.data?.items.find(item => item.id === connection.branchId)?.name ?? connection.branchId.slice(0, 8)}</td><td>{['API anahtarı', 'HMAC-SHA256', 'Authorization secret'][connection.authMode] ?? 'Bilinmiyor'}<small className="cell-sub">{connection.adapterVersion}</small></td>
          <td><span className={`pill pill--${connection.isActive ? 'green' : 'red'}`}>{connection.isActive ? 'Aktif' : 'Pasif'}</span>{connection.provider === 0 && <><small className="cell-sub">OAuth: {connection.credentialsConfigured ? `${connection.providerEnvironment === 1 ? 'Production' : 'Sandbox'} · ${connection.providerAccountId}` : 'Bekliyor'}</small>{connection.lastHealthCheckSucceeded !== null && <small className="cell-sub"><span className={`pill pill--${connection.lastHealthCheckSucceeded ? 'green' : 'red'}`}>{connection.lastHealthCheckSucceeded ? 'Bağlantı doğrulandı' : 'Bağlantı hatalı'}</span> · {new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(connection.lastHealthCheckAtUtc!))}</small>}{connection.consecutiveHealthCheckFailures > 0 && <small className="cell-sub">Ardışık hata: {connection.consecutiveHealthCheckFailures}</small>}{connection.lastHealthCheckMessage && <small className="cell-sub">{connection.lastHealthCheckMessage}</small>}</>}</td>
          <td><button className="row-action" onClick={() => void copy(connection.id, `${getIntegrationsApiUrl()}${connection.webhookPath}`)}><Copy size={13} /> {copied === connection.id ? 'Kopyalandı' : 'URL'}</button></td>
          <td className="table-actions">{canWrite && <>{connection.provider === 0 && <><button className="row-action" onClick={() => { setCredentialConnection(connection); setChainId(connection.providerAccountId ?? ''); setProviderEnvironment(String(connection.providerEnvironment)) }}><KeyRound size={13} /> OAuth ayarla</button>{connection.credentialsConfigured && <button className="row-action" onClick={() => testConnection.mutate(connection.id)} disabled={testConnection.isPending}><Wifi size={13} /> {testConnection.isPending && testConnection.variables === connection.id ? 'Test ediliyor' : 'Bağlantıyı test et'}</button>}</>}<button className="row-action" onClick={() => rotate.mutate(connection.id)} disabled={rotate.isPending}><RefreshCw size={13} /> Anahtarı yenile</button><button className="row-action" onClick={() => toggle.mutate({ id: connection.id, isActive: !connection.isActive })} disabled={toggle.isPending}>{connection.isActive ? 'Pasife al' : 'Aktifleştir'}</button></>}</td>
        </tr>)}
      </tbody></table></div>
      {visibleConnections.length === 0 && <p className="empty-state">Bu işletme için entegrasyon bağlantısı bulunmuyor.</p>}
      {(connections.error || toggle.error || rotate.error || testConnection.error) && <p className="form-error form-error--panel">{connections.error?.message ?? toggle.error?.message ?? rotate.error?.message ?? testConnection.error?.message}</p>}
    </article>

    <article className="panel orders-panel">
      <div className="panel__heading"><div><h2>Dış sisteme gönderimler</h2><p>Yemeksepeti durum senkronizasyonu ve yeniden denemeler</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Sipariş</th><th>Bağlantı</th><th>Gönderilen durum</th><th>Sonuç</th><th>Deneme</th><th>Zaman</th><th /></tr></thead><tbody>
        {outboundEvents.data?.items.map(event => <tr key={event.id}>
          <td><strong>{event.externalOrderId}</strong><small className="cell-sub">Core #{event.coreOrderId.slice(0, 8)}</small>{event.lastError && <small className="cell-sub truncate-cell">{event.lastError}</small>}</td>
          <td>{connections.data?.items.find(item => item.id === event.connectionId)?.name ?? event.connectionId.slice(0, 8)}</td>
          <td><strong>{event.providerStatus}</strong></td>
          <td><span className={`pill pill--${event.status === 2 ? 'green' : event.status >= 3 ? 'red' : 'amber'}`}>{['Bekliyor', 'Gönderiliyor', 'Gönderildi', 'Tekrar bekliyor', 'Dead letter'][event.status] ?? 'Bilinmiyor'}</span>{event.nextAttemptAtUtc && <small className="cell-sub">Sonraki: {new Intl.DateTimeFormat('tr-TR', { timeStyle: 'short' }).format(new Date(event.nextAttemptAtUtc))}</small>}</td>
          <td>{event.attempts}</td><td>{new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(event.createdAtUtc))}</td>
          <td>{canWrite && event.status >= 3 && <button className="row-action" onClick={() => retryOutbound.mutate(event.id)} disabled={retryOutbound.isPending}><RefreshCw size={13} /> Yeniden gönder</button>}</td>
        </tr>)}
      </tbody></table></div>
      {outboundEvents.data?.totalCount === 0 && <p className="empty-state">Henüz dış sisteme gönderilecek durum olayı bulunmuyor.</p>}
      {(outboundEvents.error || retryOutbound.error) && <p className="form-error form-error--panel">{outboundEvents.error?.message ?? retryOutbound.error?.message}</p>}
    </article>

    <article className="panel orders-panel">
      <div className="panel__heading"><div><h2>Son webhook olayları</h2><p>Başarılı ve yeniden denenebilir sipariş girişleri</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Olay / sipariş</th><th>Bağlantı</th><th>Durum</th><th>Deneme</th><th>Core siparişi</th><th>Zaman</th><th /></tr></thead><tbody>
        {events.data?.items.map(event => <tr key={event.id}>
          <td><strong>{event.externalOrderId}</strong><small className="cell-sub">{event.eventType} · {event.externalEventId}</small>{event.lastError && <small className="cell-sub truncate-cell">{event.lastError}</small>}</td>
          <td>{connections.data?.items.find(item => item.id === event.connectionId)?.name ?? event.connectionId.slice(0, 8)}</td>
          <td><span className={`pill pill--${event.status === 2 ? 'green' : event.status >= 3 ? 'red' : 'amber'}`}>{['Alındı', 'İşleniyor', 'Tamamlandı', 'Tekrar bekliyor', 'Dead letter'][event.status] ?? 'Bilinmiyor'}</span>{event.nextAttemptAtUtc && <small className="cell-sub">Sonraki: {new Intl.DateTimeFormat('tr-TR', { timeStyle: 'short' }).format(new Date(event.nextAttemptAtUtc))}</small>}</td>
          <td>{event.attempts}</td><td>{event.coreOrderId ? `#${event.coreOrderId.slice(0, 8)}` : '—'}</td>
          <td>{new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(event.receivedAtUtc))}</td><td>{canWrite && event.status >= 3 && <button className="row-action" onClick={() => retry.mutate(event.id)} disabled={retry.isPending}><RefreshCw size={13} /> Yeniden işle</button>}</td>
        </tr>)}
      </tbody></table></div>
      {events.data?.totalCount === 0 && <p className="empty-state">Henüz webhook olayı alınmadı.</p>}
      {(events.error || retry.error) && <p className="form-error form-error--panel">{events.error?.message ?? retry.error?.message}</p>}
    </article>
  </section>
}

function formatPercent(value: number | null | undefined) { return value === null || value === undefined ? '—' : `%${new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 }).format(value)}` }
function formatDuration(value: number | null | undefined) { if (value === null || value === undefined) return '—'; return value < 1000 ? `${Math.round(value)} ms` : `${new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 }).format(value / 1000)} sn` }
