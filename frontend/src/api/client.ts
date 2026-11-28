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
    if (!e.response) return 'The server could not be reached. Check your connection and try again.'
    const { status, data } = e.response
    const p = data && typeof data === 'object' ? (data as ProblemDetails) : undefined
    const ref = p?.traceId ? ` (reference ${p.traceId})` : ''
    if (p?.errors) return Object.values(p.errors).flat().join(' ')
    if (p?.detail || p?.title) return (p.detail ?? p.title!) + (status >= 500 ? ref : '')
    // No problem details: the answer came from IIS, a proxy or the network, not from the portal.
    if (status === 405) return 'The web server refused this request (HTTP 405). On IIS this is usually the WebDAV module: remove it for the portal site or install the latest package.'
    if (status === 413) return 'The file or request is too large for the web server (HTTP 413).'
    if (status === 502 || status === 503 || status === 504) return `The portal is not responding (HTTP ${status}). Try again in a minute; if it persists, check that the site is running.`
    return `The server answered HTTP ${status}${e.response.statusText ? ' ' + e.response.statusText : ''}. Please try again; if it persists, give this to your administrator.${ref}`
  }
  return 'Something went wrong. Please try again.'
}
