import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'

/**
 * API client. The access token lives only in memory; the refresh token is an HttpOnly cookie the browser
 * sends to /api/auth/refresh. On a 401 the client refreshes once and replays the request.
 */
let accessToken: string | null = null
let refreshing: Promise<string | null> | null = null
const listeners = new Set<(token: string | null) => void>()

export const tokenStore = {
  get: () => accessToken,
  set(token: string | null) {
    accessToken = token
    listeners.forEach((l) => l(token))
  },
  subscribe(l: (token: string | null) => void) {
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  },
}

export const api = axios.create({ baseURL: '/api', withCredentials: true, timeout: 30000 })

api.interceptors.request.use((config) => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  return config
})

export async function refreshAccessToken(): Promise<string | null> {
  refreshing ??= axios
    .post<{ accessToken: string }>('/api/auth/refresh', null, { withCredentials: true })
    .then((r) => r.data.accessToken)
    .catch(() => null)
    .finally(() => {
      refreshing = null
    })
  const token = await refreshing
  tokenStore.set(token)
  return token
}

/** Called when the API says the account must change its password or enrol MFA before doing anything else. */
export const accountGate: { onRequired: (code: string) => void } = { onRequired: () => {} }

api.interceptors.response.use(undefined, async (error: AxiosError) => {
  const code = (error.response?.data as { code?: string } | undefined)?.code
  if (error.response?.status === 403 && (code === 'mfa_enrolment_required' || code === 'password_change_required')) accountGate.onRequired(code)
  const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined
  if (error.response?.status === 401 && original && !original._retried && !original.url?.startsWith('/auth/')) {
    original._retried = true
    const token = await refreshAccessToken()
    if (token) return api(original)
  }
  return Promise.reject(error)
})

export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
  traceId?: string
}

/** Human-readable message from an API error (RFC 7807 problem details). */
export function errorMessage(e: unknown): string {
  if (axios.isAxiosError(e)) {
    const p = e.response?.data as ProblemDetails | undefined
    if (p?.errors) return Object.values(p.errors).flat().join(' ')
    if (p?.detail || p?.title) return p.detail ?? p.title!
    if (!e.response) return 'The server could not be reached. Check your connection and try again.'
  }
  return 'Something went wrong. Please try again.'
}
