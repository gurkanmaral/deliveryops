import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDownUp, Bike, ChevronLeft, ChevronRight, Clock3, FilterX, PackageCheck, Phone, Plus, RefreshCw, Search, Sparkles, X } from 'lucide-react'
import { useDeferredValue, useEffect, useRef, useState, type FormEvent } from 'react'
import { getJson, postIdempotentJson, postJson, requestJson } from '../../shared/api/httpClient'
import type { Branch, Business } from '../businesses/BusinessesPage'
import type { PagedResponse } from '../../shared/api/types'
import { useOrderRealtime } from './useOrderRealtime'
import { useAuth } from '../auth/AuthProvider'

interface Courier {
  id: string
  firstName: string
  lastName: string
  availability: number
  deliveryStatus: number
  isActive: boolean
}

interface Order {
  id: string
  businessId: string
  branchId: string
  courierId?: string
  customerName: string
  customerPhone: string
  deliveryAddress: string
  source: number
  status: number
  totalAmount: number
  createdAtUtc: string
  cancellationReason?: string
  deliveryFailureReason?: string
  allowedNextStatuses: number[]
  dispatchStatus?: number
  dispatchAttemptCount: number
  dispatchLastReason?: string
  dispatchNextAttemptAtUtc?: string
}

interface DispatchQueueItem {
  orderId: string
  businessId: string
  branchId: string
  customerName: string
  deliveryAddress: string
  createdAtUtc: string
  status: number
  attemptCount: number
  lastReason?: string
  nextAttemptAtUtc?: string
}

interface CourierSuggestion {
  rank: number
  courierId: string
  courierName: string
  isBranchCourier: boolean
  activeOrderCount: number
  distanceKm?: number
  locationRecordedAtUtc?: string
}

interface DispatchAttempt {
  id: string
  courierName?: string
  trigger: string
  wasSuccessful: boolean
  reason?: string
  createdAtUtc: string
}

const statuses = ['Yeni', 'Onaylandı', 'Kurye bekliyor', 'Atandı', 'Teslim alındı', 'Yolda', 'Teslim edildi', 'İptal', 'Teslim edilemedi', 'İade']
const sources = ['Telefon', 'Yönetici paneli', 'İşletme paneli', 'Yemeksepeti', 'Getir', 'POS', 'Diğer', 'Trendyol']
const availabilityLabels = ['Çevrimdışı', 'Müsait', 'Molada', 'Mesai dışı']
const deliveryLabels = ['Atama bekliyor', 'İşletmeye gidiyor', 'Teslimatta']

export function OrdersPage() {
  useOrderRealtime()
  const auth = useAuth()
  const client = useQueryClient()
  const businesses = useQuery({ queryKey: ['businesses'], queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const [businessId, setBusinessId] = useState('')
  const activeBusinessId = auth.user?.businessId || businessId || businesses.data?.items[0]?.id || ''
  const branches = useQuery({ queryKey: ['branches', activeBusinessId], enabled: !!activeBusinessId, queryFn: ({ signal }) => getJson<PagedResponse<Branch>>(`/api/v1/branches?businessId=${activeBusinessId}&pageSize=100`, signal) })
  const couriers = useQuery({ queryKey: ['couriers', activeBusinessId], enabled: !!activeBusinessId, queryFn: ({ signal }) => getJson<PagedResponse<Courier>>(`/api/v1/couriers?businessId=${activeBusinessId}&pageSize=100`, signal) })
  const [search, setSearch] = useState('')
  const deferredSearch = useDeferredValue(search.trim())
  const [filterBranchId, setFilterBranchId] = useState('')
  const [filterCourierId, setFilterCourierId] = useState('')
  const [filterStatus, setFilterStatus] = useState('')
  const [filterSource, setFilterSource] = useState('')
  const [createdFrom, setCreatedFrom] = useState('')
  const [createdTo, setCreatedTo] = useState('')
  const [minAmount, setMinAmount] = useState('')
  const [maxAmount, setMaxAmount] = useState('')
  const [sort, setSort] = useState('-created')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const orderQueryKey = ['orders', activeBusinessId, deferredSearch, filterBranchId, filterCourierId, filterStatus,
    filterSource, createdFrom, createdTo, minAmount, maxAmount, sort, page, pageSize]
  const orders = useQuery<PagedResponse<Order>>({
    queryKey: orderQueryKey,
    enabled: !!activeBusinessId,
    placeholderData: previous => previous,
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ businessId: activeBusinessId, page: String(page), pageSize: String(pageSize), sort })
      if (deferredSearch) params.set('search', deferredSearch)
      if (filterBranchId) params.set('branchId', filterBranchId)
      if (filterCourierId) params.set('courierId', filterCourierId)
      if (filterStatus) params.set('status', filterStatus)
      if (filterSource) params.set('source', filterSource)
      if (createdFrom) params.set('createdFrom', createdFrom)
      if (createdTo) params.set('createdTo', createdTo)
      if (minAmount) params.set('minAmount', minAmount)
      if (maxAmount) params.set('maxAmount', maxAmount)
      return getJson<PagedResponse<Order>>(`/api/v1/orders?${params}`, signal)
    },
  })
  const dispatchQueue = useQuery({ queryKey: ['dispatch-queue', activeBusinessId], enabled: !!activeBusinessId && auth.hasPermission('dispatch.read'), queryFn: ({ signal }) => getJson<PagedResponse<DispatchQueueItem>>(`/api/v1/dispatch/queue?businessId=${activeBusinessId}&pageSize=100`, signal) })
  const [branchId, setBranchId] = useState('')
  const [customerName, setCustomerName] = useState('')
  const [phone, setPhone] = useState('')
  const [address, setAddress] = useState('')
  const [amount, setAmount] = useState('')
  const createIdempotencyKey = useRef(crypto.randomUUID())
  const [selectedDispatchOrderId, setSelectedDispatchOrderId] = useState<string | null>(null)
  const canWrite = auth.hasPermission('orders.write')
  const canAssign = auth.hasPermission('orders.assign')
  const canTransition = auth.hasPermission('orders.transition')
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ['orders'] })
    void client.invalidateQueries({ queryKey: ['couriers'] })
    void client.invalidateQueries({ queryKey: ['dashboard'] })
    void client.invalidateQueries({ queryKey: ['dispatch-queue'] })
    void client.invalidateQueries({ queryKey: ['dispatch-attempts'] })
    void client.invalidateQueries({ queryKey: ['courier-suggestions'] })
  }
  const create = useMutation({
    mutationFn: () => postIdempotentJson<Order>('/api/v1/orders/phone', { businessId: activeBusinessId, branchId, customerName, customerPhone: phone, deliveryAddress: address, totalAmount: Number(amount) }, createIdempotencyKey.current),
    onSuccess: () => { createIdempotencyKey.current = crypto.randomUUID(); setCustomerName(''); setPhone(''); setAddress(''); setAmount(''); refresh() },
  })
  const changeStatus = useMutation({ mutationFn: ({ id, status }: { id: string; status: number }) => requestJson<Order>(`/api/v1/orders/${id}/status`, { method: 'PATCH', body: JSON.stringify({ status }) }), onSuccess: refresh })
  const assign = useMutation({ mutationFn: ({ id, courierId }: { id: string; courierId: string }) => requestJson<Order>(`/api/v1/orders/${id}/courier`, { method: 'PUT', body: JSON.stringify({ courierId }) }), onSuccess: refresh })
  const retryDispatch = useMutation({ mutationFn: (id: string) => postJson<void>(`/api/v1/dispatch/orders/${id}/retry`, {}), onSuccess: refresh })
  const availableOrders = dispatchQueue.data?.items ?? []
  const activeCouriers = couriers.data?.items.filter(courier => courier.isActive) ?? []

  useEffect(() => { setPage(1) }, [activeBusinessId, deferredSearch, filterBranchId, filterCourierId, filterStatus, filterSource, createdFrom, createdTo, minAmount, maxAmount, sort, pageSize])
  useEffect(() => {
    if (orders.data?.totalPages && page > orders.data.totalPages) setPage(orders.data.totalPages)
  }, [orders.data?.totalPages, page])

  function submit(event: FormEvent) { event.preventDefault(); create.mutate() }
  function clearFilters() {
    setSearch(''); setFilterBranchId(''); setFilterCourierId(''); setFilterStatus(''); setFilterSource('')
    setCreatedFrom(''); setCreatedTo(''); setMinAmount(''); setMaxAmount(''); setSort('-created'); setPage(1)
  }
  function toggleSort(field: 'created' | 'amount' | 'status' | 'source') {
    setSort(current => current === field ? `-${field}` : field)
  }
  const hasFilters = !!(search || filterBranchId || filterCourierId || filterStatus || filterSource || createdFrom || createdTo || minAmount || maxAmount || sort !== '-created')

  return <section>
    <div className="page-heading"><div><p className="eyebrow">SİPARİŞ YÖNETİMİ</p><h1>Operasyon panosu</h1><p>Telefon siparişi oluşturun, atanmamış paketleri yönetin ve teslimatı ilerletin.</p></div></div>

    <div className="dispatch-grid">
      <article className="panel dispatch-panel">
        <div className="panel__heading"><div><h2><PackageCheck size={17} className="inline-icon" /> Atama bekleyen paketler</h2><p>{availableOrders.length} paket kurye bekliyor</p></div></div>
        <div className="dispatch-list">
          {availableOrders.map(order => <div className="dispatch-item dispatch-item--operations" key={order.orderId}>
            <div><strong>#{order.orderId.slice(0, 8)} · {order.customerName}</strong><small>{order.lastReason ?? order.deliveryAddress}</small><small>{order.attemptCount} deneme · {formatWait(order.createdAtUtc)}</small></div>
            <span className="dispatch-item__actions">
              <button className="row-action row-action--primary" onClick={() => setSelectedDispatchOrderId(order.orderId)}><Sparkles size={13} /> Öneriler</button>
              {auth.hasPermission('dispatch.write') && <button className="row-action" onClick={() => retryDispatch.mutate(order.orderId)} disabled={retryDispatch.isPending}><RefreshCw size={13} /> Dene</button>}
            </span>
          </div>)}
          {availableOrders.length === 0 && <p className="empty-state">Atama bekleyen paket bulunmuyor.</p>}
        </div>
      </article>

      <article className="panel dispatch-panel">
        <div className="panel__heading"><div><h2><Bike size={17} className="inline-icon" /> Kurye kapasitesi</h2><p>{activeCouriers.length} aktif kurye</p></div></div>
        <div className="dispatch-list">
          {activeCouriers.map(courier => <div className="dispatch-item" key={courier.id}>
            <div><strong>{courier.firstName} {courier.lastName}</strong><small>{availabilityLabels[courier.availability]} · {deliveryLabels[courier.deliveryStatus]}</small></div>
            <i className={`availability-dot availability-dot--${courier.availability === 1 ? 'active' : 'idle'}`} />
          </div>)}
        </div>
      </article>
    </div>

    {selectedDispatchOrderId && <DispatchOperations orderId={selectedDispatchOrderId} onClose={() => setSelectedDispatchOrderId(null)} onAssign={courierId => assign.mutate({ id: selectedDispatchOrderId, courierId })} canAssign={canAssign} />}

    {canWrite && <article className="panel section-panel">
      <div className="panel__heading"><div><h2><Phone size={16} className="inline-icon" /> Telefon siparişi</h2><p>Çağrı merkezi üzerinden manuel kayıt</p></div></div>
      <form className="inline-form order-form" onSubmit={submit}>
        {auth.isPlatformAdmin && <label>İşletme<select value={activeBusinessId} onChange={event => { setBusinessId(event.target.value); setBranchId(''); setFilterBranchId(''); setFilterCourierId('') }} required>{businesses.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        <label>Şube<select value={branchId} onChange={event => setBranchId(event.target.value)} required><option value="">Şube seçin</option>{branches.data?.items.filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Müşteri<input value={customerName} onChange={event => setCustomerName(event.target.value)} required /></label>
        <label>Telefon<input value={phone} onChange={event => setPhone(event.target.value)} required /></label>
        <label className="inline-form__wide">Teslimat adresi<input value={address} onChange={event => setAddress(event.target.value)} required /></label>
        <label>Tutar<input type="number" min="0" step="0.01" value={amount} onChange={event => setAmount(event.target.value)} required /></label>
        <button className="primary-button" disabled={!branchId || create.isPending}><Plus size={17} /> Sipariş oluştur</button>
      </form>
      {create.error && <p className="form-error form-error--panel">{create.error.message}</p>}
    </article>}

    <article className="panel orders-panel">
      <div className="panel__heading orders-heading"><div><h2>Tüm siparişler</h2><p>{orders.data?.totalCount ?? 0} kayıt · filtreler sunucuda uygulanır</p></div>{orders.isFetching && <span className="orders-loading"><RefreshCw size={13} /> Güncelleniyor</span>}</div>
      <div className="order-filters">
        <label className="order-filter-search"><span>Arama</span><div><Search size={15} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Müşteri, telefon veya dış sipariş no" /></div></label>
        <label><span>Şube</span><select value={filterBranchId} onChange={event => setFilterBranchId(event.target.value)}><option value="">Tüm şubeler</option>{branches.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label><span>Durum</span><select value={filterStatus} onChange={event => setFilterStatus(event.target.value)}><option value="">Tüm durumlar</option>{statuses.map((label, index) => <option key={label} value={index}>{label}</option>)}</select></label>
        <label><span>Kaynak</span><select value={filterSource} onChange={event => setFilterSource(event.target.value)}><option value="">Tüm kaynaklar</option>{sources.map((label, index) => <option key={label} value={index}>{label}</option>)}</select></label>
        <label><span>Kurye</span><select value={filterCourierId} onChange={event => setFilterCourierId(event.target.value)}><option value="">Tüm kuryeler</option>{activeCouriers.map(courier => <option key={courier.id} value={courier.id}>{courier.firstName} {courier.lastName}</option>)}</select></label>
        <label><span>Başlangıç</span><input type="date" value={createdFrom} max={createdTo || undefined} onChange={event => setCreatedFrom(event.target.value)} /></label>
        <label><span>Bitiş</span><input type="date" value={createdTo} min={createdFrom || undefined} onChange={event => setCreatedTo(event.target.value)} /></label>
        <label><span>Min. tutar</span><input type="number" min="0" step="0.01" value={minAmount} onChange={event => setMinAmount(event.target.value)} placeholder="₺0" /></label>
        <label><span>Maks. tutar</span><input type="number" min={minAmount || '0'} step="0.01" value={maxAmount} onChange={event => setMaxAmount(event.target.value)} placeholder="₺∞" /></label>
        <label><span>Sıralama</span><select value={sort} onChange={event => setSort(event.target.value)}><option value="-created">En yeni</option><option value="created">En eski</option><option value="-amount">Tutar: yüksek</option><option value="amount">Tutar: düşük</option><option value="status">Durum</option><option value="source">Kaynak</option></select></label>
        <button className="secondary-button order-filter-clear" onClick={clearFilters} disabled={!hasFilters}><FilterX size={15} /> Temizle</button>
      </div>
      {orders.error && <p className="form-error form-error--panel">{orders.error.message}</p>}
      <div className="table-wrap"><table><thead><tr><th><button className="table-sort" onClick={() => toggleSort('created')}>Sipariş <ArrowDownUp size={12} /></button></th><th>Müşteri</th><th>Adres</th><th>Kurye</th><th><button className="table-sort" onClick={() => toggleSort('status')}>Durum <ArrowDownUp size={12} /></button></th><th>Kaynak</th><th>İşlem</th></tr></thead><tbody>
        {orders.data?.items.map(order => <tr key={order.id}>
          <td><strong>#{order.id.slice(0, 8)}</strong><small className="cell-sub">₺{order.totalAmount.toFixed(2)} · {new Date(order.createdAtUtc).toLocaleDateString('tr-TR')}</small></td>
          <td>{order.customerName}<small className="cell-sub">{order.customerPhone}</small></td>
          <td className="truncate-cell">{order.deliveryAddress}</td>
          <td>{canAssign ? <select className="table-select" value={order.courierId ?? ''} onChange={event => event.target.value && assign.mutate({ id: order.id, courierId: event.target.value })} disabled={order.status !== 1 && order.status !== 2 && order.status !== 3}><option value="">Kurye seçin</option>{activeCouriers.map(courier => <option key={courier.id} value={courier.id}>{courier.firstName} {courier.lastName}</option>)}</select> : activeCouriers.find(x => x.id === order.courierId)?.firstName ?? 'Atanmadı'}</td>
          <td><span className={`pill pill--${order.status === 6 ? 'green' : order.status === 7 || order.status === 8 ? 'red' : order.status === 2 ? 'amber' : 'blue'}`}>{statuses[order.status]}</span></td>
          <td><span className="order-source">{sources[order.source]}</span></td>
          <td>{canTransition ? <OrderAction order={order} onChange={status => changeStatus.mutate({ id: order.id, status })} /> : <span className="muted">—</span>}</td>
        </tr>)}
      </tbody></table></div>
      {!orders.isLoading && orders.data?.items.length === 0 && <p className="empty-state order-empty">Filtrelere uygun sipariş bulunamadı.</p>}
      <div className="order-pagination">
        <label>Sayfa başına <select value={pageSize} onChange={event => setPageSize(Number(event.target.value))}><option value="25">25</option><option value="50">50</option><option value="100">100</option></select></label>
        <span>{orders.data?.totalCount ? `${(page - 1) * pageSize + 1}–${Math.min(page * pageSize, orders.data.totalCount)} / ${orders.data.totalCount}` : '0 kayıt'}</span>
        <div><button className="icon-button" aria-label="Önceki sayfa" disabled={page <= 1 || orders.isFetching} onClick={() => setPage(current => Math.max(1, current - 1))}><ChevronLeft size={17} /></button><b>{page} / {Math.max(1, orders.data?.totalPages ?? 1)}</b><button className="icon-button" aria-label="Sonraki sayfa" disabled={page >= (orders.data?.totalPages ?? 1) || orders.isFetching} onClick={() => setPage(current => current + 1)}><ChevronRight size={17} /></button></div>
      </div>
      {(assign.error || changeStatus.error) && <p className="form-error form-error--panel">{assign.error?.message ?? changeStatus.error?.message}</p>}
    </article>
  </section>
}

function DispatchOperations({ orderId, onClose, onAssign, canAssign }: { orderId: string; onClose(): void; onAssign(courierId: string): void; canAssign: boolean }) {
  const suggestions = useQuery({ queryKey: ['courier-suggestions', orderId], queryFn: ({ signal }) => getJson<PagedResponse<CourierSuggestion>>(`/api/v1/dispatch/orders/${orderId}/suggestions?pageSize=100`, signal) })
  const attempts = useQuery({ queryKey: ['dispatch-attempts', orderId], queryFn: ({ signal }) => getJson<PagedResponse<DispatchAttempt>>(`/api/v1/dispatch/orders/${orderId}/attempts?pageSize=100`, signal) })
  return <article className="panel dispatch-operations">
    <div className="panel__heading"><div><h2><Sparkles size={17} className="inline-icon" /> Kurye önerileri</h2><p>#{orderId.slice(0, 8)} için canlı kapasite ve konum sıralaması</p></div><button className="icon-button" onClick={onClose} aria-label="Kapat"><X size={18} /></button></div>
    <div className="dispatch-operations__grid">
      <div className="suggestion-list">
        {suggestions.data?.items.map(item => <div className="suggestion-row" key={item.courierId}><span className="suggestion-rank">{item.rank}</span><div><strong>{item.courierName}</strong><small>{item.isBranchCourier ? 'Şube kuryesi' : 'İşletme kuryesi'} · {item.activeOrderCount} aktif paket · {item.distanceKm == null ? 'Konum yok' : `${item.distanceKm} km`}</small></div>{canAssign && <button className="row-action row-action--primary" onClick={() => onAssign(item.courierId)}>Ata</button>}</div>)}
        {suggestions.isLoading && <p className="empty-state">Uygun kuryeler hesaplanıyor…</p>}
        {!suggestions.isLoading && suggestions.data?.totalCount === 0 && <p className="empty-state">Şu anda kurallara uyan kurye yok.</p>}
      </div>
      <div className="attempt-list"><h3><Clock3 size={15} /> Atama geçmişi</h3>{attempts.data?.items.map(item => <div className="attempt-row" key={item.id}><i className={item.wasSuccessful ? 'dot dot--green' : 'dot dot--red'} /><span><strong>{triggerLabel(item.trigger)}</strong><small>{item.courierName ?? item.reason ?? 'Kurye seçilemedi'} · {new Date(item.createdAtUtc).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}</small></span></div>)}{attempts.data?.totalCount === 0 && <p className="empty-state">Henüz atama denemesi yok.</p>}</div>
    </div>
  </article>
}

function formatWait(createdAtUtc: string) {
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(createdAtUtc).getTime()) / 60000))
  return minutes < 1 ? 'az önce' : `${minutes} dk bekliyor`
}

function triggerLabel(trigger: string) {
  const labels: Record<string, string> = { Automatic: 'Otomatik atama', ManualRetry: 'Manuel tekrar', ManualAssignment: 'Manuel atama', ManualReassignment: 'Kurye değişimi', SelfClaim: 'Kurye üstlendi' }
  return labels[trigger] ?? trigger
}

function OrderAction({ order, onChange }: { order: Order; onChange(status: number): void }) {
  const next: Record<number, { status: number; label: string }> = {
    0: { status: 1, label: 'Onayla' }, 1: { status: 2, label: 'Kurye beklet' },
    3: { status: 4, label: 'Teslim aldı' }, 4: { status: 5, label: 'Yola çıkar' },
    5: { status: 6, label: 'Teslim et' }, 8: { status: 5, label: 'Tekrar dene' },
  }
  const action = next[order.status]
  return action && order.allowedNextStatuses.includes(action.status)
    ? <button className="row-action row-action--primary" onClick={() => onChange(action.status)}>{action.label}</button>
    : <span className="muted">—</span>
}
