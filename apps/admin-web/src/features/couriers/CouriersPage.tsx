import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bike, Plus } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { getJson, postJson, requestJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import { useAuth } from '../auth/AuthProvider'
import type { Branch, Business } from '../businesses/BusinessesPage'

interface Courier { id: string; businessId: string; branchId?: string; firstName: string; lastName: string; phoneNumber: string; availability: number; deliveryStatus: number; isActive: boolean }

export function CouriersPage() {
  const auth = useAuth()
  const client = useQueryClient()
  const businesses = useQuery({ queryKey: ['businesses'], queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const [businessId, setBusinessId] = useState('')
  const activeBusinessId = auth.user?.businessId || businessId || businesses.data?.items[0]?.id || ''
  const branches = useQuery({ queryKey: ['branches', activeBusinessId], enabled: !!activeBusinessId, queryFn: ({ signal }) => getJson<PagedResponse<Branch>>(`/api/v1/branches?businessId=${activeBusinessId}&pageSize=100`, signal) })
  const couriers = useQuery({ queryKey: ['couriers', activeBusinessId], enabled: !!activeBusinessId, queryFn: ({ signal }) => getJson<PagedResponse<Courier>>(`/api/v1/couriers?businessId=${activeBusinessId}&pageSize=100`, signal) })
  const [branchId, setBranchId] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [phone, setPhone] = useState('')
  const canWrite = auth.hasPermission('couriers.write')
  const create = useMutation({ mutationFn: () => postJson<Courier>('/api/v1/couriers', { businessId: activeBusinessId, branchId: branchId || null, firstName, lastName, phoneNumber: phone }), onSuccess: () => { setFirstName(''); setLastName(''); setPhone(''); void client.invalidateQueries({ queryKey: ['couriers', activeBusinessId] }) } })
  const deactivate = useMutation({ mutationFn: (id: string) => requestJson<void>(`/api/v1/couriers/${id}`, { method: 'DELETE' }), onSuccess: () => void client.invalidateQueries({ queryKey: ['couriers', activeBusinessId] }) })
  const submit = (event: FormEvent) => { event.preventDefault(); create.mutate() }

  return <section><div className="page-heading"><div><p className="eyebrow">SAHA EKİBİ</p><h1>Kuryeler</h1><p>{auth.isPlatformAdmin ? 'Kuryeleri işletme ve şubelere atayın.' : 'İşletmenize bağlı kurye ekibini yönetin.'}</p></div></div><article className="panel section-panel">
    {canWrite && <form className="inline-form courier-form" onSubmit={submit}>
      {auth.isPlatformAdmin && <label>İşletme<select value={activeBusinessId} onChange={e => { setBusinessId(e.target.value); setBranchId('') }} required>{businesses.data?.items.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>}
      <label>Şube<select value={branchId} onChange={e => setBranchId(e.target.value)}><option value="">Tüm şubeler</option>{branches.data?.items.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>Ad<input value={firstName} onChange={e => setFirstName(e.target.value)} required /></label><label>Soyad<input value={lastName} onChange={e => setLastName(e.target.value)} required /></label><label>Telefon<input value={phone} onChange={e => setPhone(e.target.value)} required /></label><button className="primary-button" disabled={!activeBusinessId || create.isPending}><Plus size={17} /> Kurye ekle</button>
    </form>}
    {create.error && <p className="form-error form-error--panel">{create.error.message}</p>}
    <div className="table-wrap"><table><thead><tr><th>Kurye</th><th>Telefon</th><th>Şube</th><th>Müsaitlik</th><th>Teslimat</th>{canWrite && <th />}</tr></thead><tbody>{couriers.data?.items.map(courier => <tr key={courier.id}><td><strong><Bike size={14} className="inline-icon" /> {courier.firstName} {courier.lastName}</strong></td><td>{courier.phoneNumber}</td><td>{branches.data?.items.find(x => x.id === courier.branchId)?.name ?? 'Genel'}</td><td><span className={`pill pill--${courier.availability === 1 ? 'green' : 'amber'}`}>{['Çevrimdışı', 'Müsait', 'Molada', 'Vardiya dışı'][courier.availability]}</span></td><td>{['Paket bekliyor', 'Alıma gidiyor', 'Teslimatta'][courier.deliveryStatus]}</td>{canWrite && <td><button className="row-action" onClick={() => deactivate.mutate(courier.id)}>Pasife al</button></td>}</tr>)}</tbody></table></div>
  </article></section>
}
