import { useState, type FormEvent } from 'react'
import { Bike, LockKeyhole, Mail } from 'lucide-react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from './AuthProvider'

export function LoginPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState(import.meta.env.DEV ? 'admin@deliveryops.local' : '')
  const [password, setPassword] = useState(import.meta.env.DEV ? 'DeliveryOps123!' : '')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  if (auth.isAuthenticated) return <Navigate to="/" replace />

  async function submit(event: FormEvent) {
    event.preventDefault(); setError(''); setSubmitting(true)
    try {
      await auth.login(email, password)
      const destination = (location.state as { from?: { pathname?: string } } | null)?.from?.pathname ?? '/'
      navigate(destination, { replace: true })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Giriş başarısız oldu.')
    } finally { setSubmitting(false) }
  }

  function selectDemoAccount(type: 'platform' | 'business') {
    setEmail(type === 'platform' ? 'admin@deliveryops.local' : 'business@demo.local')
    setPassword(type === 'platform' ? 'DeliveryOps123!' : 'BusinessDemo123!')
    setError('')
  }

  return <main className="login-page"><section className="login-card"><div className="login-brand"><span className="brand__mark"><Bike size={21} /></span><strong>DeliveryOps</strong></div><p className="eyebrow">OPERASYON MERKEZİ</p><h1>Tekrar hoş geldiniz</h1><p className="login-copy">Sipariş ve kurye ağınızı yönetmek için giriş yapın.</p>{import.meta.env.DEV && <div className="demo-account-picker"><button type="button" onClick={() => selectDemoAccount('platform')}>Platform yöneticisi</button><button type="button" onClick={() => selectDemoAccount('business')}>İşletme yöneticisi</button></div>}<form onSubmit={submit}><label>E-posta<div className="field"><Mail size={17} /><input type="email" value={email} onChange={e => setEmail(e.target.value)} required /></div></label><label>Şifre<div className="field"><LockKeyhole size={17} /><input type="password" value={password} onChange={e => setPassword(e.target.value)} required /></div></label>{error && <div className="form-error">{error}</div>}<button className="primary-button login-submit" disabled={submitting}>{submitting ? 'Giriş yapılıyor…' : 'Giriş yap'}</button></form><small className="login-hint">Geliştirme için yukarıdan bir demo rolü seçebilirsiniz.</small></section></main>
}
