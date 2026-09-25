import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, MapPin, Plus } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { getJson, postJson, requestJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import { useAuth } from '../auth/AuthProvider'

export interface Business { id: string; name: string; code: string; isActive: boolean; branchCount: number }
export interface Branch { id: string; businessId: string; name: string; address: string; isActive: boolean }

export function BusinessesPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const businesses = useQuery({ queryKey: ['businesses'], queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const [selectedId, setSelectedId] = useState('')
  const activeBusinessId = auth.user?.businessId || selectedId || businesses.data?.items[0]?.id || ''
  const activeBusiness = businesses.data?.items.find(x => x.id === activeBusinessId)
  const branches = useQuery({ queryKey: ['branches', activeBusinessId], enabled: !!activeBusinessId, queryFn: ({ signal }) => getJson<PagedResponse<Branch>>(`/api/v1/branches?businessId=${activeBusinessId}&pageSize=100`, signal) })
  const [businessName, setBusinessName] = useState('')
  const [businessCode, setBusinessCode] = useState('')
  const [branchName, setBranchName] = useState('')
  const [branchAddress, setBranchAddress] = useState('')
  const canManageBranches = auth.hasPermission('branches.write')

  const createBusiness = useMutation({
    mutationFn: () => postJson<Business>('/api/v1/businesses', { name: businessName, code: businessCode }),
    onSuccess: business => { setBusinessName(''); setBusinessCode(''); setSelectedId(business.id); void queryClient.invalidateQueries({ queryKey: ['businesses'] }) },
  })
  const createBranch = useMutation({
    mutationFn: () => postJson<Branch>('/api/v1/branches', { businessId: activeBusinessId, name: branchName, address: branchAddress, latitude: null, longitude: null }),
    onSuccess: () => { setBranchName(''); setBranchAddress(''); void queryClient.invalidateQueries({ queryKey: ['branches', activeBusinessId] }); void queryClient.invalidateQueries({ queryKey: ['businesses'] }) },
  })
  const deactivateBranch = useMutation({
    mutationFn: (id: string) => requestJson<void>(`/api/v1/branches/${id}`, { method: 'DELETE' }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['branches', activeBusinessId] }),
  })

  function submitBusiness(event: FormEvent) { event.preventDefault(); createBusiness.mutate() }
  function submitBranch(event: FormEvent) { event.preventDefault(); createBranch.mutate() }

  return <section>
    <div className="page-heading"><div><p className="eyebrow">{auth.isPlatformAdmin ? 'YÖNETİM' : 'İŞLETME AYARLARI'}</p><h1>{auth.isPlatformAdmin ? 'İşletmeler ve şubeler' : activeBusiness?.name ?? 'İşletmem'}</h1><p>{auth.isPlatformAdmin ? 'Tenant kayıtlarını ve operasyon noktalarını yönetin.' : 'Şubelerinizi ve teslimat noktalarınızı yönetin.'}</p></div></div>

    {auth.isPlatformAdmin ? <div className="management-grid">
      <article className="panel form-panel"><div className="panel__heading"><div><h2>Yeni işletme</h2><p>Platforma yeni bir tenant ekleyin</p></div><Building2 size={20} /></div><form className="entity-form" onSubmit={submitBusiness}><label>İşletme adı<input value={businessName} onChange={e => setBusinessName(e.target.value)} required /></label><label>İşletme kodu<input value={businessCode} onChange={e => setBusinessCode(e.target.value.toUpperCase())} placeholder="MOLA-01" required /></label>{createBusiness.error && <p className="form-error">{createBusiness.error.message}</p>}<button className="primary-button" disabled={createBusiness.isPending}><Plus size={17} /> İşletme ekle</button></form></article>
      <article className="panel list-panel"><div className="panel__heading"><div><h2>İşletmeler</h2><p>{businesses.data?.totalCount ?? 0} kayıt</p></div></div><div className="entity-list">{businesses.data?.items.map(business => <button key={business.id} className={`entity-row ${activeBusinessId === business.id ? 'entity-row--active' : ''}`} onClick={() => setSelectedId(business.id)}><span className="entity-icon"><Building2 size={18} /></span><span><strong>{business.name}</strong><small>{business.code} · {business.branchCount} şube</small></span><i className={business.isActive ? 'dot dot--green' : 'dot dot--red'} /></button>)}</div></article>
    </div> : <article className="panel section-panel"><div className="panel__heading"><div><h2>{activeBusiness?.name ?? 'İşletme bilgisi'}</h2><p>{activeBusiness?.code ?? '—'} · {activeBusiness?.branchCount ?? 0} şube</p></div><span className={`pill pill--${activeBusiness?.isActive ? 'green' : 'red'}`}>{activeBusiness?.isActive ? 'Aktif' : 'Pasif'}</span></div></article>}

    <article className="panel section-panel"><div className="panel__heading"><div><h2>Şubeler</h2><p>{auth.isPlatformAdmin ? 'Seçili işletmenin teslimat noktaları' : 'İşletmenizin teslimat noktaları'}</p></div></div>
      {canManageBranches && <form className="inline-form" onSubmit={submitBranch}><label>Şube adı<input value={branchName} onChange={e => setBranchName(e.target.value)} required disabled={!activeBusinessId} /></label><label className="inline-form__wide">Adres<input value={branchAddress} onChange={e => setBranchAddress(e.target.value)} required disabled={!activeBusinessId} /></label><button className="primary-button" disabled={!activeBusinessId || createBranch.isPending}><Plus size={17} /> Şube ekle</button></form>}
      {createBranch.error && <p className="form-error form-error--panel">{createBranch.error.message}</p>}
      <div className="table-wrap"><table><thead><tr><th>Şube</th><th>Adres</th><th>Durum</th>{canManageBranches && <th />}</tr></thead><tbody>{branches.data?.items.map(branch => <tr key={branch.id}><td><strong><MapPin size={14} className="inline-icon" /> {branch.name}</strong></td><td>{branch.address}</td><td><span className={`pill pill--${branch.isActive ? 'green' : 'red'}`}>{branch.isActive ? 'Aktif' : 'Pasif'}</span></td>{canManageBranches && <td><button className="row-action" onClick={() => deactivateBranch.mutate(branch.id)} disabled={!branch.isActive}>Pasife al</button></td>}</tr>)}</tbody></table></div>
    </article>
  </section>
}
