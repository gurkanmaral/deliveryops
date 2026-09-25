import { useQueryClient } from '@tanstack/react-query'
import { createContext, type PropsWithChildren, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { getJson, onAuthenticationLost, postJson, requestJson, setAccessToken } from '../../shared/api/httpClient'

export interface CurrentUser {
  id: string
  email: string
  firstName: string
  lastName: string
  businessId: string | null
  branchId: string | null
  courierId: string | null
  roles: string[]
  permissions: string[]
}

interface AuthContextValue {
  isAuthenticated: boolean
  isLoading: boolean
  user: CurrentUser | null
  isPlatformAdmin: boolean
  hasPermission(permission: string): boolean
  login(email: string, password: string): Promise<void>
  logout(): Promise<void>
}

interface AuthResponse { accessToken: string; expiresAtUtc: string }
const AuthContext = createContext<AuthContextValue | null>(null)
const getCurrentUser = () => getJson<CurrentUser>('/api/v1/auth/me', undefined, 'auth')

export function AuthProvider({ children }: PropsWithChildren) {
  const queryClient = useQueryClient()
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [isLoading, setLoading] = useState(true)

  useEffect(() => {
    const unsubscribe = onAuthenticationLost(() => {
      queryClient.clear()
      setUser(null)
    })
    requestJson<AuthResponse>('/api/v1/auth/refresh', { api: 'auth', method: 'POST' })
      .then(async response => { setAccessToken(response.accessToken); setUser(await getCurrentUser()) })
      .catch(() => { setAccessToken(null); setUser(null) })
      .finally(() => setLoading(false))
    return unsubscribe
  }, [queryClient])

  const login = useCallback(async (email: string, password: string) => {
    queryClient.clear()
    const response = await postJson<AuthResponse>('/api/v1/auth/login', { email, password }, 'auth')
    setAccessToken(response.accessToken)
    try { setUser(await getCurrentUser()) }
    catch (error) { setAccessToken(null); throw error }
  }, [queryClient])

  const logout = useCallback(async () => {
    try { await requestJson<void>('/api/v1/auth/logout', { api: 'auth', method: 'POST' }) } finally {
      setAccessToken(null)
      queryClient.clear()
      setUser(null)
    }
  }, [queryClient])

  const value = useMemo<AuthContextValue>(() => ({
    isAuthenticated: user !== null,
    isLoading,
    user,
    isPlatformAdmin: user?.roles.includes('PlatformAdmin') ?? false,
    hasPermission: permission => user?.permissions.includes(permission) ?? false,
    login,
    logout,
  }), [isLoading, login, logout, user])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used within AuthProvider.')
  return value
}
