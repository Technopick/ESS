import { api } from './client'

export interface IntegrationInstanceStatus {
  settingsId: string
  name: string
  platform: string
  server: string | null
  enabled: boolean
  simulator: boolean
  lastHealthCheckAtUtc: string | null
  lastHealthCheckOk: boolean | null
  lastHealthCheckMessage: string | null
  companies: string[]
}

export interface IntegrationDashboard {
  instances: IntegrationInstanceStatus[]
  lastSuccessfulSyncUtc: string | null
  lastFailedSyncUtc: string | null
  pending: number
  processing: number
  succeeded24h: number
  failed: number
  deadLetter: number
  requiresReconciliation: number
  averageResponseMs24h: number | null
  availability24h: number | null
  recentErrors: { occurredAtUtc: string; operation: string; error: string | null; statusCode: number | null }[]
}

export interface EndpointProbe {
  logicalName: string
  protocol: string
  serviceName: string
  reachable: boolean
  httpStatus: number | null
  message: string | null
}

export interface ConnectionTestResult {
  success: boolean
  message: string
  navVersion: string | null
  latencyMs: number
  endpoints: EndpointProbe[]
}

export type QueueStatus = 'Pending' | 'Processing' | 'Succeeded' | 'Failed' | 'DeadLetter' | 'RequiresReconciliation' | 'Cancelled'
const queueStatusByIndex: QueueStatus[] = ['Pending', 'Processing', 'Succeeded', 'Failed', 'DeadLetter', 'RequiresReconciliation', 'Cancelled']

export interface QueueItem {
  id: string
  operation: string
  entityType: string
  idempotencyKey: string
  status: QueueStatus
  attempts: number
  maxAttempts: number
  createdAtUtc: string
  nextAttemptAtUtc: string
  completedAtUtc: string | null
  navDocumentNo: string | null
  lastError: string | null
  lastErrorCategory: string | null
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export const integrationApi = {
  status: () => api.get<IntegrationDashboard>('/integration/nav/status').then((r) => r.data),
  test: (settingsId: string) => api.post<ConnectionTestResult>('/integration/nav/test', { settingsId }).then((r) => r.data),
  sync: (companyId: string, full: boolean) => api.post('/integration/nav/sync', { companyId, full }).then((r) => r.data),
  transactions: (status?: number, page = 1) =>
    api
      .get<Paged<Omit<QueueItem, 'status'> & { status: number | QueueStatus }>>('/integration/nav/transactions', { params: { status, page, pageSize: 25 } })
      .then((r) => ({
        ...r.data,
        items: r.data.items.map((i) => ({ ...i, status: typeof i.status === 'number' ? queueStatusByIndex[i.status] : i.status })),
      })),
  retry: (id: string) => api.post(`/integration/nav/retry/${id}`).then((r) => r.data),
}
