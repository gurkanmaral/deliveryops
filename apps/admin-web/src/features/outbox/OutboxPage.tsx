import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, BellRing, CheckCircle2, Inbox, PlugZap, RotateCcw } from 'lucide-react'
import { useMemo, useState } from 'react'
import { getJson, postJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import { useAuth } from '../auth/AuthProvider'

type QueueKind = 'integration' | 'notification'

interface OutboxItem {
  id: string
  businessId: string
  orderId: string
  eventType: string
  attempts: number
  lastHttpStatusCode?: number
  lastError?: string
  createdAtUtc: string
  nextAttemptAtUtc: string
  processedAtUtc?: string
  deadLetteredAtUtc?: string
}

interface QueueItem extends OutboxItem { kind: QueueKind }

const dateFormatter = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' })

export function OutboxPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<'all' | QueueKind>('all')
  const integrations = useQuery({
    queryKey: ['core-integration-outbox'],
    queryFn: ({ signal }) => getJson<PagedResponse<OutboxItem>>('/api/v1/operations/integration-outbox?pageSize=100', signal),
    refetchInterval: 30_000,
  })
  const notifications = useQuery({
    queryKey: ['core-notification-outbox'],
    queryFn: ({ signal }) => getJson<PagedResponse<OutboxItem>>('/api/v1/operations/notification-outbox?pageSize=100', signal),
    refetchInterval: 30_000,
  })
  const retry = useMutation({
    mutationFn: ({ kind, id }: Pick<QueueItem, 'kind' | 'id'>) =>
      postJson<{ id: string; nextAttemptAtUtc: string }>(`/api/v1/operations/${kind}-outbox/${id}/retry`, {}),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['core-integration-outbox'] })
      void queryClient.invalidateQueries({ queryKey: ['core-notification-outbox'] })
    },
  })

  const items = useMemo<QueueItem[]>(() => [
    ...(integrations.data?.items ?? []).map(item => ({ ...item, kind: 'integration' as const })),
    ...(notifications.data?.items ?? []).map(item => ({ ...item, kind: 'notification' as const })),
  ].filter(item => filter === 'all' || item.kind === filter)
    .sort((left, right) => Date.parse(right.deadLetteredAtUtc ?? right.createdAtUtc) - Date.parse(left.deadLetteredAtUtc ?? left.createdAtUtc)),
  [filter, integrations.data, notifications.data])

  const isLoading = integrations.isLoading || notifications.isLoading
  const error = integrations.error ?? notifications.error
  const totalAttempts = [...(integrations.data?.items ?? []), ...(notifications.data?.items ?? [])]
    .reduce((total, item) => total + item.attempts, 0)

  return <section>
    <div className="page-heading">
      <div><p className="eyebrow">SİSTEM GÜVENİLİRLİĞİ</p><h1>Hata kuyrukları</h1><p>Otomatik teslim edilemeyen entegrasyon ve kurye bildirimlerini inceleyip yeniden işleyin.</p></div>
      <span className="outbox-auto-refresh"><span className="status-dot" /> 30 saniyede yenilenir</span>
    </div>

    <div className="outbox-summary-grid">
      <article><span><Inbox size={17} /> Toplam dead-letter</span><strong>{(integrations.data?.totalCount ?? 0) + (notifications.data?.totalCount ?? 0)}</strong><small>Operatör incelemesi bekliyor</small></article>
      <article><span><PlugZap size={17} /> Entegrasyon</span><strong>{integrations.data?.totalCount ?? 0}</strong><small>Sağlayıcı durum olayları</small></article>
      <article><span><BellRing size={17} /> Bildirim</span><strong>{notifications.data?.totalCount ?? 0}</strong><small>Kurye push olayları</small></article>
      <article><span><RotateCcw size={17} /> Toplam deneme</span><strong>{totalAttempts}</strong><small>Dead-letter öncesi girişimler</small></article>
    </div>

    <div className="outbox-tabs" role="tablist" aria-label="Kuyruk filtresi">
      <button className={filter === 'all' ? 'active' : ''} onClick={() => setFilter('all')}>Tümü</button>
      <button className={filter === 'integration' ? 'active' : ''} onClick={() => setFilter('integration')}>Entegrasyon</button>
      <button className={filter === 'notification' ? 'active' : ''} onClick={() => setFilter('notification')}>Bildirim</button>
    </div>

    {error && <p className="form-error form-error--panel">{error.message}</p>}
    <article className="panel outbox-panel">
      <div className="panel__heading"><div><h2>İşlem bekleyen kayıtlar</h2><p>Kalıcı hata veya maksimum retry sınırına ulaşan mesajlar</p></div><span>{items.length} kayıt</span></div>
      {!isLoading && items.length === 0 && <div className="outbox-empty"><CheckCircle2 size={34} /><h3>Kuyruklar temiz</h3><p>Operatör müdahalesi bekleyen mesaj bulunmuyor.</p></div>}
      {isLoading && <p className="empty-state">Kuyruklar yükleniyor…</p>}
      {items.length > 0 && <div className="outbox-list">{items.map(item => {
        const isRetrying = retry.isPending && retry.variables?.id === item.id
        return <div className="outbox-row" key={`${item.kind}-${item.id}`}>
          <span className={`outbox-row__icon outbox-row__icon--${item.kind}`}>{item.kind === 'integration' ? <PlugZap size={18} /> : <BellRing size={18} />}</span>
          <div className="outbox-row__main">
            <div><strong>{item.kind === 'integration' ? 'Entegrasyon olayı' : 'Kurye bildirimi'}</strong><span className="pill pill--warning">{item.eventType}</span></div>
            <p>{item.lastError || 'Hata detayı bulunmuyor.'}</p>
            <small>Sipariş: {item.orderId.slice(0, 8)} · İşletme: {item.businessId.slice(0, 8)} · {dateFormatter.format(new Date(item.deadLetteredAtUtc ?? item.createdAtUtc))}</small>
          </div>
          <div className="outbox-row__status"><strong>{item.attempts}</strong><small>deneme</small>{item.lastHttpStatusCode && <span>HTTP {item.lastHttpStatusCode}</span>}</div>
          {auth.hasPermission('integrations.write') && <button className="secondary-button" onClick={() => retry.mutate({ kind: item.kind, id: item.id })} disabled={retry.isPending}><RotateCcw size={14} /> {isRetrying ? 'Başlatılıyor…' : 'Tekrar dene'}</button>}
        </div>
      })}</div>}
    </article>
    {retry.error && <p className="form-error form-error--panel"><AlertTriangle size={14} /> {retry.error.message}</p>}
  </section>
}
