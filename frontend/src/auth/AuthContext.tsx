import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, refreshAccessToken, tokenStore } from '../api/client'

export interface Me {
  userName: string | null
  roles: string[]
  permissions: string[]
  companyId: string | null
  employeeId: string | null
}

export type LoginOutcome = { done: true } | { done: false; mfaChallenge: string }

interface AuthState {
  me: Me | null
  loading: boolean
  /** Signs in with a password. When MFA is on, returns the challenge to pass to verifyMfa. */
  login: (userName: string, password: string) => Promise<LoginOutcome>
  verifyMfa: (challenge: string, code: string) => Promise<void>
  reload: () => Promise<void>
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
    async (userName: string, password: string): Promise<LoginOutcome> => {
      const { data } = await api.post<{ accessToken: string | null; mfaRequired: boolean; mfaChallenge: string | null }>('/auth/login', {
        userName,
        password,
      })
      if (data.mfaRequired && data.mfaChallenge) return { done: false, mfaChallenge: data.mfaChallenge }
      tokenStore.set(data.accessToken)
      await loadMe()
      return { done: true }
    },
    [loadMe],
  )

  const verifyMfa = useCallback(
    async (challenge: string, code: string) => {
      const { data } = await api.post<{ accessToken: string }>('/auth/mfa', { mfaChallenge: challenge, code })
      tokenStore.set(data.accessToken)
      await loadMe()
    },
    [loadMe],
  )

  /** Gets a fresh access token (e.g. after enrolling MFA, so new claims apply) and reloads the profile. */
  const reload = useCallback(async () => {
    const t = await refreshAccessToken()
    if (t) await loadMe()
  }, [loadMe])

  const logout = useCallback(async () => {
    try {
      await api.post('/auth/logout')
    } finally {
      tokenStore.set(null)
      setMe(null)
    }
  }, [])

  const value = useMemo<AuthState>(
    () => ({ me, loading, login, verifyMfa, reload, logout, can: (p) => !!me?.permissions.includes(p) }),
    [me, loading, login, verifyMfa, reload, logout],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

export const Permissions = {
  IntegrationView: 'integration.nav.view',
  IntegrationOperate: 'integration.nav.operate',
  DashboardManagement: 'dashboard.management',
  EmployeesViewAll: 'employees.view.all',
  ProfileViewOwn: 'profile.view.own',
  ProfileRequestChange: 'profile.change.request',
  EmployeesViewDirectory: 'employees.view.directory',
  UsersManage: 'users.manage',
  LeaveViewOwn: 'leave.view.own',
  PayrollViewOwn: 'payroll.view.own',
  PayrollPublish: 'payroll.publish',
  LeaveApply: 'leave.apply',
  LeaveViewTeam: 'leave.view.team',
  ExpenseSubmit: 'expense.submit',
  ExpenseViewAll: 'expense.view.all',
  DocumentsViewOwn: 'documents.view.own',
  DocumentsManage: 'documents.manage',
  RequestsSubmit: 'requests.submit',
  RequestsManage: 'requests.manage',
  AttendanceClock: 'attendance.clock',
  AttendanceViewTeam: 'attendance.view.team',
  TrainingApply: 'training.apply',
  TrainingManage: 'training.manage',
  ApprovalsAct: 'approvals.act',
  ApprovalsMonitor: 'approvals.monitor',
  WorkflowConfigure: 'workflow.configure',
} as const
