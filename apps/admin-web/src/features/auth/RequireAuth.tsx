import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from './AuthProvider'

export function RequireAuth() {
  const auth = useAuth()
  const location = useLocation()
  if (auth.isLoading) return <div className="auth-loading">Oturum kontrol ediliyor…</div>
  return auth.isAuthenticated ? <Outlet /> : <Navigate to="/login" replace state={{ from: location }} />
}
