import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, refreshAccessToken, tokenStore } from '../api/client'

export interface Me {
  userName: string | null
  roles: string[]
  permissions: string[]
  companyId: string | null
  employeeId: string | null
}

interface AuthState {
  me: Me | null
  loading: boolean
  login: (userName: string, password: string) => Promise<void>
  logout: () => Promise<void>
  can: (permission: string) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)

  const loadMe = useCallback(async () => {
    const { data } = await api.get<Me>('/auth/me')
    setMe(data)
  }, [])

  // Restore the session from the HttpOnly refresh cookie on first load.
  useEffect(() => {
    refreshAccessToken()
      .then((t) => (t ? loadMe() : undefined))
      .catch(() => setMe(null))
      .finally(() => setLoading(false))
    return tokenStore.subscribe((t) => {
      if (!t) setMe(null)
    })
  }, [loadMe])

  const login = useCallback(
    async (userName: string, password: string) => {
      const { data } = await api.post<{ accessToken: string }>('/auth/login', { userName, password })
      tokenStore.set(data.accessToken)
      await loadMe()
    },
    [loadMe],
  )

  const logout = useCallback(async () => {
    try {
      await api.post('/auth/logout')
    } finally {
      tokenStore.set(null)
      setMe(null)
    }
  }, [])

  const value = useMemo<AuthState>(
    () => ({ me, loading, login, logout, can: (p) => !!me?.permissions.includes(p) }),
    [me, loading, login, logout],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

// eslint-disable-next-line react-refresh/only-export-components
export const Permissions = {
  IntegrationView: 'integration.nav.view',
  IntegrationOperate: 'integration.nav.operate',
  DashboardManagement: 'dashboard.management',
  EmployeesViewAll: 'employees.view.all',
  ProfileViewOwn: 'profile.view.own',
} as const
