import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, UserRoundCog } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { getJson, postJson, requestJson } from '../../shared/api/httpClient'
import type { PagedResponse } from '../../shared/api/types'
import { useAuth } from '../auth/AuthProvider'
import type { Branch, Business } from '../businesses/BusinessesPage'

interface ManagedUser {
  id: string
  email: string
  firstName: string
  lastName: string
  businessId: string | null
  branchId: string | null
  isActive: boolean
  roles: string[]
}

const roleLabels: Record<string, string> = {
  PlatformAdmin: 'Platform yöneticisi',
  BusinessAdmin: 'İşletme yöneticisi',
  BusinessStaff: 'İşletme personeli',
  Courier: 'Kurye',
}

export function UsersPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const businesses = useQuery({ queryKey: ['businesses'], queryFn: ({ signal }) => getJson<PagedResponse<Business>>('/api/v1/businesses?pageSize=100', signal) })
  const [selectedBusinessId, setSelectedBusinessId] = useState('')
  const businessId = auth.user?.businessId || selectedBusinessId || businesses.data?.items[0]?.id || ''
  const branches = useQuery({
    queryKey: ['branches', businessId],
    enabled: !!businessId,
    queryFn: ({ signal }) => getJson<PagedResponse<Branch>>(`/api/v1/branches?businessId=${businessId}&pageSize=100`, signal),
  })
  const users = useQuery({
    queryKey: ['users', businessId],
    enabled: !!businessId,
    queryFn: ({ signal }) => getJson<PagedResponse<ManagedUser>>(`/api/v1/users?businessId=${businessId}&pageSize=100`, signal, 'auth'),
  })
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [branchId, setBranchId] = useState('')
  const [role, setRole] = useState('BusinessStaff')

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['users', businessId] })
  const create = useMutation({
    mutationFn: () => postJson<ManagedUser>('/api/v1/users', {
      email, password, firstName, lastName, role,
      businessId, branchId: branchId || null, courierId: null,
    }, 'auth'),
    onSuccess: () => {
      setFirstName(''); setLastName(''); setEmail(''); setPassword(''); setBranchId(''); setRole('BusinessStaff'); refresh()
    },
  })
  const setActive = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => requestJson<ManagedUser>(`/api/v1/users/${id}/active`, {
      api: 'auth', method: 'PATCH', body: JSON.stringify({ isActive }),
    }),
    onSuccess: refresh,
  })

  function submit(event: FormEvent) { event.preventDefault(); create.mutate() }

  return <section>
    <div className="page-heading"><div><p className="eyebrow">ERİŞİM YÖNETİMİ</p><h1>Kullanıcılar</h1><p>{auth.isPlatformAdmin ? 'İşletme yöneticilerini ve personel hesaplarını yönetin.' : 'İşletmenizin panel kullanıcılarını yönetin.'}</p></div></div>

    <article className="panel section-panel">
      <div className="panel__heading"><div><h2><UserRoundCog size={17} className="inline-icon" /> Yeni kullanıcı</h2><p>Hesap ilk girişte hemen kullanılabilir</p></div></div>
      <form className="inline-form user-form" onSubmit={submit}>
        {auth.isPlatformAdmin && <label>İşletme<select value={businessId} onChange={event => { setSelectedBusinessId(event.target.value); setBranchId('') }} required>{businesses.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        <label>Rol<select value={role} onChange={event => setRole(event.target.value)}>{auth.isPlatformAdmin && <option value="BusinessAdmin">İşletme yöneticisi</option>}<option value="BusinessStaff">İşletme personeli</option></select></label>
        <label>Şube<select value={branchId} onChange={event => setBranchId(event.target.value)}><option value="">Tüm şubeler</option>{branches.data?.items.filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Ad<input value={firstName} onChange={event => setFirstName(event.target.value)} required /></label>
        <label>Soyad<input value={lastName} onChange={event => setLastName(event.target.value)} required /></label>
        <label>E-posta<input type="email" value={email} onChange={event => setEmail(event.target.value)} required /></label>
        <label>Geçici şifre<input type="password" minLength={10} value={password} onChange={event => setPassword(event.target.value)} required /></label>
        <button className="primary-button" disabled={!businessId || create.isPending}><Plus size={17} /> Kullanıcı ekle</button>
      </form>
      {create.error && <p className="form-error form-error--panel">{create.error.message}</p>}
    </article>

    <article className="panel orders-panel">
      <div className="panel__heading"><div><h2>Panel kullanıcıları</h2><p>{users.data?.totalCount ?? 0} hesap</p></div></div>
      <div className="table-wrap"><table><thead><tr><th>Kullanıcı</th><th>E-posta</th><th>Rol</th><th>Şube</th><th>Durum</th><th /></tr></thead><tbody>
        {users.data?.items.map(user => {
          const canManage = auth.isPlatformAdmin || (user.roles.length === 1 && user.roles[0] === 'BusinessStaff')
          return <tr key={user.id}>
          <td><strong>{user.firstName} {user.lastName}</strong></td>
          <td>{user.email}</td>
          <td>{user.roles.map(item => roleLabels[item] ?? item).join(', ')}</td>
          <td>{branches.data?.items.find(item => item.id === user.branchId)?.name ?? 'Tüm şubeler'}</td>
          <td><span className={`pill pill--${user.isActive ? 'green' : 'red'}`}>{user.isActive ? 'Aktif' : 'Pasif'}</span></td>
          <td>{user.id === auth.user?.id ? <span className="muted">Mevcut hesap</span> : canManage ? <button className="row-action" disabled={setActive.isPending} onClick={() => setActive.mutate({ id: user.id, isActive: !user.isActive })}>{user.isActive ? 'Pasife al' : 'Aktifleştir'}</button> : <span className="muted">Bu rol yönetilemez</span>}</td>
        </tr>})}
      </tbody></table></div>
      {users.data?.totalCount === 0 && <p className="empty-state">Bu işletmede henüz panel kullanıcısı yok.</p>}
      {(users.error || setActive.error) && <p className="form-error form-error--panel">{users.error?.message ?? setActive.error?.message}</p>}
    </article>
  </section>
}
