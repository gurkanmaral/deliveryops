import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from './AuthProvider'

interface RequirePermissionProps extends PropsWithChildren {
  permission?: string
  platformOnly?: boolean
}

export function RequirePermission({ children, permission, platformOnly = false }: RequirePermissionProps) {
  const auth = useAuth()
  const isAllowed = (!platformOnly || auth.isPlatformAdmin) && (!permission || auth.hasPermission(permission))
  return isAllowed ? children : <Navigate to="/" replace />
}
