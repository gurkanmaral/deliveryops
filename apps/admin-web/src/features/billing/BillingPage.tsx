import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Calculator, CheckCircle2, CreditCard, Download, FileCheck2, PlusCircle, ReceiptText, RotateCcw, Save, Settings2, WalletCards } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { downloadFile, getJson, postIdempotentJson, postJson, requestJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import type { Business } from '../businesses/BusinessesPage'
import { useAuth } from '../auth/AuthProvider'

interface BillingSettings {
  businessId: string; feePerDeliveredOrder: number; commissionRatePercent: number
  feePerReturnedOrder: number; taxRatePercent: number; currency: string
}
interface BillingAmounts {
  deliveredOrderCount: number; returnedOrderCount: number; cancelledOrderCount: number
  deliveredOrderValue: number; feePerDeliveredOrder: number; commissionRatePercent: number
  feePerReturnedOrder: number; taxRatePercent: number; deliveryFeeAmount: number
  commissionAmount: number; returnFeeAmount: number; subtotalAmount: number
  taxAmount: number; totalAmount: number; currency: string
}
interface BillingPreview extends BillingAmounts { businessId: string; businessName: string; periodFrom: string; periodTo: string }
interface BillingSettlement extends BillingPreview { id: string; status: number; createdAtUtc: string; finalizedAtUtc?: string; documentNumber: string }
interface CreditAccount { businessId: string; businessName: string; balance: number; lifetimeAdded: number; lifetimeConsumed: number; lowBalanceThreshold: number; isLowBalance: boolean; updatedAtUtc?: string }
interface CreditPackage { code: string; name: string; amount: number }
interface CreditTransaction { id: string; type: number; amount: number; balanceAfter: number; orderId?: string; description: string; createdAtUtc: string }

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })
const integer = new Intl.NumberFormat('tr-TR')
const dateLabel = new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })

export function BillingPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [selectedBusinessId, setSelectedBusinessId] = useState(auth.user?.businessId ?? '')
  const [period] = useState(currentMonth)
  const [from, setFrom] = useState(period.from)
  const [to, setTo] = useState(period.to)
  const businesses = useQuery({ queryKey: ['businesses'], enabled: auth.isPlatformAdmin, queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const businessId = auth.user?.businessId || selectedBusinessId || businesses.data?.items[0]?.id || ''
  const settings = useQuery({ queryKey: ['billing-settings', businessId], enabled: !!businessId, queryFn: ({ signal }) => getJson<BillingSettings>(`/api/v1/billing/settings?businessId=${businessId}`, signal) })
  const preview = useQuery({ queryKey: ['billing-preview', businessId, from, to], enabled: !!businessId && from <= to, queryFn: ({ signal }) => getJson<BillingPreview>(`/api/v1/billing/preview?businessId=${businessId}&from=${from}&to=${to}`, signal) })
  const settlements = useQuery({ queryKey: ['billing-settlements', businessId], enabled: !!businessId, queryFn: ({ signal }) => getJson<PagedResponse<BillingSettlement>>(`/api/v1/billing/settlements?businessId=${businessId}&pageSize=100`, signal) })
  const creditAccount = useQuery({ queryKey: ['credit-account', businessId], enabled: !!businessId && auth.hasPermission('credits.read'), queryFn: ({ signal }) => getJson<CreditAccount>(`/api/v1/credits/account?businessId=${businessId}`, signal) })
  const creditPackages = useQuery({ queryKey: ['credit-packages'], enabled: auth.hasPermission('credits.read'), queryFn: ({ signal }) => getJson<PagedResponse<CreditPackage>>('/api/v1/credits/packages?pageSize=100', signal) })
  const creditTransactions = useQuery({ queryKey: ['credit-transactions', businessId], enabled: !!businessId && auth.hasPermission('credits.read'), queryFn: ({ signal }) => getJson<PagedResponse<CreditTransaction>>(`/api/v1/credits/transactions?businessId=${businessId}&pageSize=20`, signal) })
  const [form, setForm] = useState<BillingSettings | null>(null)
  const [selectedPackage, setSelectedPackage] = useState('CREDIT_1000')
  const [creditDescription, setCreditDescription] = useState('')
  const [adjustmentAmount, setAdjustmentAmount] = useState(0)
  const [adjustmentDescription, setAdjustmentDescription] = useState('')
  const [refundOrderId, setRefundOrderId] = useState('')
  const [refundDescription, setRefundDescription] = useState('')
  const [lowBalanceThreshold, setLowBalanceThreshold] = useState(100)
  useEffect(() => { if (settings.data) setForm(settings.data) }, [settings.data])
  useEffect(() => { if (creditAccount.data) setLowBalanceThreshold(creditAccount.data.lowBalanceThreshold) }, [creditAccount.data])

  const saveSettings = useMutation({
    mutationFn: () => requestJson<BillingSettings>('/api/v1/billing/settings', { method: 'PUT', body: JSON.stringify({ ...form, businessId }) }),
    onSuccess: data => { setForm(data); void queryClient.invalidateQueries({ queryKey: ['billing-settings', businessId] }); void queryClient.invalidateQueries({ queryKey: ['billing-preview', businessId] }) },
  })
  const createSettlement = useMutation({
    mutationFn: () => postJson<BillingSettlement>('/api/v1/billing/settlements', { businessId, from, to }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['billing-settlements', businessId] }),
  })
  const finalizeSettlement = useMutation({
    mutationFn: (id: string) => postJson<BillingSettlement>(`/api/v1/billing/settlements/${id}/finalize`, {}),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['billing-settlements', businessId] }),
  })
  const topUpCredits = useMutation({
    mutationFn: () => postIdempotentJson<CreditAccount>('/api/v1/credits/top-up', { businessId, packageCode: selectedPackage, description: creditDescription || null }, crypto.randomUUID()),
    onSuccess: () => { setCreditDescription(''); void queryClient.invalidateQueries({ queryKey: ['credit-account', businessId] }); void queryClient.invalidateQueries({ queryKey: ['credit-transactions', businessId] }) },
  })
  const adjustCredits = useMutation({
    mutationFn: () => postIdempotentJson<CreditAccount>('/api/v1/credits/adjustments', { businessId, amount: adjustmentAmount, description: adjustmentDescription }, crypto.randomUUID()),
    onSuccess: () => { setAdjustmentAmount(0); setAdjustmentDescription(''); refreshCredits() },
  })
  const refundCredit = useMutation({
    mutationFn: () => postIdempotentJson<CreditAccount>('/api/v1/credits/refunds', { businessId, orderId: refundOrderId, description: refundDescription }, crypto.randomUUID()),
    onSuccess: () => { setRefundOrderId(''); setRefundDescription(''); refreshCredits() },
  })
  const saveCreditSettings = useMutation({
    mutationFn: () => requestJson<CreditAccount>('/api/v1/credits/settings', { method: 'PUT', body: JSON.stringify({ businessId, lowBalanceThreshold }) }),
    onSuccess: () => refreshCredits(),
  })

  function refreshCredits() { void queryClient.invalidateQueries({ queryKey: ['credit-account', businessId] }); void queryClient.invalidateQueries({ queryKey: ['credit-transactions', businessId] }); void queryClient.invalidateQueries({ queryKey: ['operational-alerts'] }) }

  function updateAmount(key: keyof BillingSettings, value: string) { setForm(current => current ? { ...current, [key]: Number(value) } : current) }
  function submitSettings(event: FormEvent) { event.preventDefault(); saveSettings.mutate() }
  const totals = preview.data

  async function downloadDocument(item: BillingSettlement, format: 'pdf' | 'xlsx') {
    await downloadFile(`/api/v1/billing/settlements/${item.id}/document?format=${format}`, `${item.documentNumber}.${format}`)
  }

  return <section>
    <div className="page-heading"><div><p className="eyebrow">FİNANSAL OPERASYON</p><h1>Mutabakat ve fiyatlandırma</h1><p>Teslimat ücretlerini tanımlayın, dönem borcunu ön izleyin ve kesinleştirin.</p></div>{auth.hasPermission('billing.write') && <button className="primary-button" disabled={!preview.data || createSettlement.isPending} onClick={() => createSettlement.mutate()}><FileCheck2 size={17} /> Taslağı kaydet</button>}</div>

    <div className="panel billing-toolbar">
      {auth.isPlatformAdmin && <label>İşletme<select value={businessId} onChange={event => setSelectedBusinessId(event.target.value)}>{businesses.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
      <label>Dönem başlangıcı<input type="date" value={from} max={to} onChange={event => setFrom(event.target.value)} /></label>
      <label>Dönem sonu<input type="date" value={to} min={from} onChange={event => setTo(event.target.value)} /></label>
      <span><Calculator size={15} /> Teslimatın tamamlandığı güne göre hesaplanır.</span>
    </div>

    {preview.error && <p className="form-error billing-error">{preview.error.message}</p>}
    {createSettlement.error && <p className="form-error billing-error">{createSettlement.error.message}</p>}
    {finalizeSettlement.error && <p className="form-error billing-error">{finalizeSettlement.error.message}</p>}
    {topUpCredits.error && <p className="form-error billing-error">{topUpCredits.error.message}</p>}
    {adjustCredits.error && <p className="form-error billing-error">{adjustCredits.error.message}</p>}
    {refundCredit.error && <p className="form-error billing-error">{refundCredit.error.message}</p>}
    {saveCreditSettings.error && <p className="form-error billing-error">{saveCreditSettings.error.message}</p>}

      {auth.hasPermission('credits.read') && <><article className="panel credit-panel"><div className={`credit-balance${creditAccount.data?.isLowBalance ? ' credit-balance--low' : ''}`}><span><CreditCard size={20} /> Kredi bakiyesi</span><strong>{integer.format(creditAccount.data?.balance ?? 0)}</strong><small>{(creditAccount.data?.balance ?? 0) < 0 ? 'Eksi bakiye · sonraki yüklemede mahsup edilir. Panel siparişleri durdu; platform ve kasa siparişleri -20 krediye kadar alınır.' : creditAccount.data?.isLowBalance ? `Düşük bakiye · eşik ${integer.format(creditAccount.data.lowBalanceThreshold)}` : 'Her yeni sipariş 1 kredi kullanır.'}</small><div><span>Toplam yüklenen <b>{integer.format(creditAccount.data?.lifetimeAdded ?? 0)}</b></span><span>Kullanılan <b>{integer.format(creditAccount.data?.lifetimeConsumed ?? 0)}</b></span></div></div>{auth.hasPermission('credits.write') && <form onSubmit={event => { event.preventDefault(); topUpCredits.mutate() }}><label>Kredi paketi<select value={selectedPackage} onChange={event => setSelectedPackage(event.target.value)}>{creditPackages.data?.items.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label><label>Açıklama<input value={creditDescription} onChange={event => setCreditDescription(event.target.value)} placeholder="Örn. Eylül paketi" /></label><button className="primary-button" disabled={topUpCredits.isPending}><PlusCircle size={16} /> Paketi yükle</button></form>}<div className="credit-activity"><h3>Son kredi hareketleri</h3>{creditTransactions.data?.items.slice(0, 5).map(item => <div key={item.id}><span className={item.amount > 0 ? 'credit-positive' : 'credit-negative'}>{item.amount > 0 ? '+' : ''}{item.amount}</span><span><strong>{item.description}</strong><small>{creditType(item.type)} · {new Date(item.createdAtUtc).toLocaleString('tr-TR')} · Bakiye {item.balanceAfter}</small></span></div>)}{creditTransactions.data?.totalCount === 0 && <p>Henüz kredi hareketi bulunmuyor.</p>}</div></article>{auth.hasPermission('credits.write') && <article className="panel credit-admin-tools"><form onSubmit={event => { event.preventDefault(); adjustCredits.mutate() }}><h3><Settings2 size={16} /> Manuel düzeltme</h3><label>Miktar (+ / -)<input type="number" min="-1000000" max="1000000" value={adjustmentAmount} onChange={event => setAdjustmentAmount(Number(event.target.value))} /></label><label>Zorunlu açıklama<input required value={adjustmentDescription} onChange={event => setAdjustmentDescription(event.target.value)} /></label><button className="row-action row-action--primary" disabled={!adjustmentAmount || !adjustmentDescription || adjustCredits.isPending}>Düzeltmeyi uygula</button></form><form onSubmit={event => { event.preventDefault(); refundCredit.mutate() }}><h3><RotateCcw size={16} /> Sipariş kredisi iadesi</h3><label>Sipariş ID<input required value={refundOrderId} onChange={event => setRefundOrderId(event.target.value)} /></label><label>Zorunlu açıklama<input required value={refundDescription} onChange={event => setRefundDescription(event.target.value)} /></label><button className="row-action row-action--primary" disabled={!refundOrderId || !refundDescription || refundCredit.isPending}>1 krediyi iade et</button></form><form onSubmit={event => { event.preventDefault(); saveCreditSettings.mutate() }}><h3><CreditCard size={16} /> Düşük bakiye uyarısı</h3><label>Uyarı eşiği<input type="number" min="0" max="1000000" value={lowBalanceThreshold} onChange={event => setLowBalanceThreshold(Number(event.target.value))} /></label><small>0 değeri uyarıyı kapatır.</small><button className="row-action row-action--primary" disabled={saveCreditSettings.isPending}>Eşiği kaydet</button></form></article>}</>}
    <div className="billing-summary-grid">
      <article className="billing-total-card"><span><WalletCards size={18} /> Dönem toplamı</span><strong>{totals ? money.format(totals.totalAmount) : '—'}</strong><small>KDV dahil platform hizmet bedeli</small></article>
      <article><span>Teslim edilen</span><strong>{totals ? integer.format(totals.deliveredOrderCount) : '—'}</strong><small>{totals ? money.format(totals.deliveredOrderValue) : '—'} sipariş değeri</small></article>
      <article><span>İade</span><strong>{totals ? integer.format(totals.returnedOrderCount) : '—'}</strong><small>{totals ? money.format(totals.returnFeeAmount) : '—'} işlem bedeli</small></article>
      <article><span>İptal</span><strong>{totals ? integer.format(totals.cancelledOrderCount) : '—'}</strong><small>Ücretlendirme dışı</small></article>
    </div>

    <div className="billing-grid">
      <article className="panel billing-breakdown"><div className="panel__heading"><div><h2>Dönem dökümü</h2><p>{preview.data?.businessName ?? 'İşletme'} · {periodText(from, to)}</p></div><ReceiptText size={20} /></div>
        <div className="billing-lines">
          <Line label={`${totals?.deliveredOrderCount ?? 0} teslimat × ${money.format(totals?.feePerDeliveredOrder ?? 0)}`} value={totals?.deliveryFeeAmount} />
          <Line label={`Teslim edilen sipariş tutarı × %${formatRate(totals?.commissionRatePercent)}`} value={totals?.commissionAmount} />
          <Line label={`${totals?.returnedOrderCount ?? 0} iade × ${money.format(totals?.feePerReturnedOrder ?? 0)}`} value={totals?.returnFeeAmount} />
          <Line label="Ara toplam" value={totals?.subtotalAmount} strong />
          <Line label={`KDV (%${formatRate(totals?.taxRatePercent)})`} value={totals?.taxAmount} />
          <Line label="Genel toplam" value={totals?.totalAmount} total />
        </div>
      </article>

      <article className="panel billing-settings"><div className="panel__heading"><div><h2>Fiyatlandırma</h2><p>İşletmeye uygulanacak tarife</p></div></div>
        {form && <form onSubmit={submitSettings}><label>Teslim edilen paket başı ücret<div><input type="number" min="0" step="0.01" value={form.feePerDeliveredOrder} disabled={!auth.hasPermission('billing.write')} onChange={event => updateAmount('feePerDeliveredOrder', event.target.value)} /><span>₺</span></div></label><label>Sipariş tutarı komisyonu<div><input type="number" min="0" max="100" step="0.01" value={form.commissionRatePercent} disabled={!auth.hasPermission('billing.write')} onChange={event => updateAmount('commissionRatePercent', event.target.value)} /><span>%</span></div></label><label>İade paket işlem ücreti<div><input type="number" min="0" step="0.01" value={form.feePerReturnedOrder} disabled={!auth.hasPermission('billing.write')} onChange={event => updateAmount('feePerReturnedOrder', event.target.value)} /><span>₺</span></div></label><label>KDV oranı<div><input type="number" min="0" max="100" step="0.01" value={form.taxRatePercent} disabled={!auth.hasPermission('billing.write')} onChange={event => updateAmount('taxRatePercent', event.target.value)} /><span>%</span></div></label>{auth.hasPermission('billing.write') && <button className="primary-button" disabled={saveSettings.isPending}><Save size={16} /> Tarifeyi kaydet</button>}{saveSettings.isSuccess && <span className="save-success">Tarife güncellendi.</span>}{saveSettings.error && <span className="form-error">{saveSettings.error.message}</span>}</form>}
      </article>
    </div>

    <article className="panel billing-history" aria-label="Mutabakat geçmişi"><div className="panel__heading"><div><h2>Mutabakat geçmişi</h2><p>Kaydedilmiş ve kesinleşmiş dönemler</p></div></div><div className="table-wrap"><table><thead><tr>{auth.isPlatformAdmin && <th>İşletme</th>}<th>Belge / dönem</th><th>Teslim / İade</th><th>Ara toplam</th><th>KDV</th><th>Genel toplam</th><th>Durum</th><th /></tr></thead><tbody>{settlements.data?.items.map(item => <tr key={item.id}>{auth.isPlatformAdmin && <td>{item.businessName}</td>}<td><strong>{item.documentNumber || periodText(item.periodFrom, item.periodTo)}</strong>{item.documentNumber && <small className="cell-sub">{periodText(item.periodFrom, item.periodTo)}</small>}</td><td>{item.deliveredOrderCount} / {item.returnedOrderCount}</td><td>{money.format(item.subtotalAmount)}</td><td>{money.format(item.taxAmount)}</td><td><strong>{money.format(item.totalAmount)}</strong></td><td><span className={`pill pill--${item.status === 1 ? 'green' : 'amber'}`}>{item.status === 1 ? 'Kesinleşti' : 'Taslak'}</span></td><td><div className="billing-row-actions">{item.status === 0 && auth.hasPermission('billing.write') && <button className="row-action row-action--primary" disabled={finalizeSettlement.isPending} onClick={() => finalizeSettlement.mutate(item.id)}><CheckCircle2 size={14} /> Kesinleştir</button>}{item.status === 1 && <><button className="row-action" onClick={() => void downloadDocument(item, 'pdf')}><Download size={13} /> PDF</button><button className="row-action" onClick={() => void downloadDocument(item, 'xlsx')}><Download size={13} /> Excel</button></>}</div></td></tr>)}</tbody></table>{settlements.data?.totalCount === 0 && <p className="empty-state">Henüz kaydedilmiş mutabakat bulunmuyor.</p>}</div></article>
  </section>
}

function Line({ label, value, strong, total }: { label: string; value?: number; strong?: boolean; total?: boolean }) { return <div className={total ? 'billing-line billing-line--total' : strong ? 'billing-line billing-line--strong' : 'billing-line'}><span>{label}</span><b>{value === undefined ? '—' : money.format(value)}</b></div> }
function currentMonth() { const now = new Date(); return { from: localDate(new Date(now.getFullYear(), now.getMonth(), 1)), to: localDate(now) } }
function localDate(date: Date) { return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}` }
function periodText(from: string, to: string) { return `${dateLabel.format(new Date(`${from}T12:00:00`))} – ${dateLabel.format(new Date(`${to}T12:00:00`))}` }
function formatRate(value?: number) { return value === undefined ? '—' : new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 2 }).format(value) }
function creditType(value: number) { return ['Paket yükleme', 'Sipariş kullanımı', 'Düzeltme', 'İade'][value] ?? 'Kredi işlemi' }
