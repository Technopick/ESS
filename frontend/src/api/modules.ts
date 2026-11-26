import { api } from './client'
import type { LeaveBalance } from './types'

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number }
export type RequestStatus = 'Draft' | 'Submitted' | 'InApproval' | 'ReturnedForCorrection' | 'Approved' | 'Rejected' | 'Cancelled' | 'Withdrawn' | 'Completed'
export type NavSyncStatus = 'NotRequired' | 'PendingIntegration' | 'InProgress' | 'Posted' | 'Failed' | 'RequiresReconciliation'

const get = <T,>(url: string, params?: object) => api.get<T>(url, { params }).then((r) => r.data)
const post = <T = void,>(url: string, body?: unknown) => api.post<T>(url, body ?? {}).then((r) => r.data)
const put = <T = void,>(url: string, body?: unknown) => api.put<T>(url, body).then((r) => r.data)
const del = (url: string) => api.delete(url).then(() => undefined)

// ---------------------------------------------------------------- leave
export interface LeaveType { id: string; code: string; name: string; allowHalfDay: boolean; requiresAttachment: boolean; minNoticeDays: number; maxDaysPerApplication: number | null; countsWorkingDaysOnly: boolean }
export interface LeaveInput { leaveTypeId: string; startDate: string; endDate: string; startHalfDay: boolean; endHalfDay: boolean; reason?: string | null; relieverEmployeeId?: string | null }
export interface LeaveCheck { days: number; available: number | null; availableAfter: number | null; errors: string[]; warnings: string[]; balanceFromCache: boolean; isValid: boolean }
export interface LeaveView {
  id: string; reference: string; leaveTypeId: string; leaveType: string; startDate: string; endDate: string; startHalfDay: boolean; endHalfDay: boolean
  days: number; reason: string | null; relieverEmployeeId: string | null; reliever: string | null; status: RequestStatus; navStatus: NavSyncStatus
  navDocumentNo: string | null; workflowInstanceId: string | null; submittedAtUtc: string | null; createdAtUtc: string; employeeName: string | null
}
export interface CalendarEntry { employeeId: string; employeeName: string; department: string | null; startDate: string; endDate: string; startHalfDay: boolean; endHalfDay: boolean; leaveType: string | null; status: RequestStatus }

export const leaveApi = {
  types: () => get<LeaveType[]>('/leave/types'),
  balances: () => get<{ balances: LeaveBalance[]; retrievedAtUtc: string | null; fromCache: boolean; navMessage: string | null }>('/leave/balances'),
  check: (input: LeaveInput, applicationId?: string) => api.post<LeaveCheck>('/leave/check', input, { params: { applicationId } }).then((r) => r.data),
  submit: (input: LeaveInput) => post<LeaveView>('/leave', input),
  resubmit: (id: string, input: LeaveInput) => put<LeaveView>(`/leave/${id}`, input),
  cancel: (id: string, reason?: string) => post<LeaveView>(`/leave/${id}/cancel`, { reason }),
  mine: (page = 1) => get<Paged<LeaveView>>('/leave/mine', { page }),
  get: (id: string) => get<LeaveView>(`/leave/${id}`),
  calendar: (from: string, to: string) => get<CalendarEntry[]>('/leave/calendar', { from, to }),
}

// ---------------------------------------------------------------- approvals
export interface InboxItem { taskId: string; instanceId: string; documentType: string; reference: string; summary: string | null; requester: string; step: string; assignedAtUtc: string; dueAtUtc: string | null; overdue: boolean; onBehalfOf: string | null; escalated: boolean; amount: number | null }
export interface DecidedItem { taskId: string; instanceId: string; documentType: string; reference: string; summary: string | null; requester: string; decision: string; comment: string | null; actedAtUtc: string | null; requestStatus: string }
export interface InstanceView {
  id: string; documentType: string; documentId: string; reference: string; summary: string | null; linkUrl: string | null; requester: string; status: string
  createdAtUtc: string; completedAtUtc: string | null; details: { label: string; value: string | null }[]
  pendingWith: { step: string; approver: string; dueAtUtc: string | null }[]
  timeline: { action: string; actor: string; onBehalfOf: string | null; comment: string | null; occurredAtUtc: string }[]
  myTaskId: string | null
}
export interface Delegation { id: string; toUserId: string; toUser: string; startsAtUtc: string; endsAtUtc: string; documentType: string | null; reason: string | null; active: boolean }
export interface MonitorItem { instanceId: string; documentType: string; reference: string; summary: string | null; requester: string; submittedAtUtc: string; pendingWith: string | null; dueAtUtc: string | null; overdue: boolean }

export const approvalsApi = {
  inbox: () => get<InboxItem[]>('/approvals/inbox'),
  decided: (page = 1) => get<Paged<DecidedItem>>('/approvals/decided', { page }),
  instance: (id: string) => get<InstanceView>(`/approvals/instances/${id}`),
  decide: (taskId: string, decision: 'approve' | 'reject' | 'return', comment?: string) => post(`/approvals/tasks/${taskId}/${decision}`, { comment }),
  delegations: () => get<Delegation[]>('/approvals/delegations'),
  delegate: (body: { toUserId: string; startsAtUtc: string; endsAtUtc: string; documentType: string | null; reason: string | null }) => post('/approvals/delegations', body),
  endDelegation: (id: string) => del(`/approvals/delegations/${id}`),
  approvers: (search?: string) => get<{ userId: string; name: string }[]>('/approvals/approvers', { search }),
  monitor: (overdueOnly: boolean, page = 1) => get<Paged<MonitorItem>>('/approvals/monitor', { overdueOnly, page }),
}

// ---------------------------------------------------------------- notifications
export interface NotificationItem { id: string; eventCode: string; subject: string | null; body: string; linkUrl: string | null; createdAtUtc: string; read: boolean }
export const notificationsApi = {
  list: (page = 1, unreadOnly = false) => get<Paged<NotificationItem>>('/notifications', { page, unreadOnly }),
  unreadCount: () => get<{ count: number }>('/notifications/unread-count'),
  read: (id: string) => post(`/notifications/${id}/read`),
  readAll: () => post('/notifications/read-all'),
}

// ---------------------------------------------------------------- payroll
export interface PayrollPeriod { id: string; code: string; name: string; startDate: string; endDate: string; payDate: string | null; closed: boolean; published: boolean; publishedAtUtc: string | null }
export interface PayslipLine { code: string; description: string; group: string; amount: number; yearToDate: number | null }
export interface Payslip {
  periodCode: string; periodName: string; paymentDate: string | null; currency: string; employeeNo: string; employeeName: string; jobTitle: string | null; department: string | null
  basicPay: number; grossPay: number; totalDeductions: number; netPay: number; lines: PayslipLine[]; fromCache: boolean; retrievedAtUtc: string; pdfAvailable: boolean
}
export interface StatutorySummary {
  year: number; codes: string[]; labels: Record<string, string>; totals: Record<string, number>; totalGross: number; totalTaxable: number
  months: { periodCode: string; periodName: string; grossPay: number; taxablePay: number; amounts: Record<string, number>; available: boolean }[]
}
export const payrollApi = {
  periods: () => get<PayrollPeriod[]>('/payroll/periods'),
  payslip: (code: string) => get<Payslip>(`/payroll/payslips/${encodeURIComponent(code)}`),
  pdf: (code: string) => api.get<Blob>(`/payroll/payslips/${encodeURIComponent(code)}/pdf`, { responseType: 'blob' }).then((r) => r.data),
  statutory: (year: number) => get<StatutorySummary>('/payroll/statutory', { year }),
  adminPeriods: () => get<PayrollPeriod[]>('/admin/payroll/periods'),
  publish: (id: string, notify: boolean) => post(`/admin/payroll/periods/${id}/publish`, { notify }),
  unpublish: (id: string) => post(`/admin/payroll/periods/${id}/unpublish`),
}

// ---------------------------------------------------------------- documents
export interface DocumentCategory { id: string; code: string; name: string; employeeCanUpload: boolean; retentionMonths: number | null }
export interface DocumentItem {
  id: string; title: string; categoryId: string; category: string; employeeId: string | null; employeeName: string | null; visibility: string; expiryDate: string | null
  currentVersion: number; fileName: string; contentType: string; sizeBytes: number; uploadedAtUtc: string; uploadedBy: string; scanned: boolean; canDelete: boolean
}
export const documentsApi = {
  categories: () => get<DocumentCategory[]>('/documents/categories'),
  mine: () => get<DocumentItem[]>('/documents/mine'),
  upload: (file: File, fields: Record<string, string | undefined>) => {
    const form = new FormData()
    form.append('file', file)
    Object.entries(fields).forEach(([k, v]) => v !== undefined && v !== '' && form.append(k, v))
    return api.post<DocumentItem>('/documents', form).then((r) => r.data)
  },
  newVersion: (id: string, file: File, expiryDate?: string) => {
    const form = new FormData()
    form.append('file', file)
    if (expiryDate) form.append('expiryDate', expiryDate)
    return api.post(`/documents/${id}/versions`, form).then(() => undefined)
  },
  uploadReceipt: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api.post<{ id: string }>('/documents/receipts', form).then((r) => r.data)
  },
  remove: (id: string) => del(`/documents/${id}`),
  /** Downloads through the API (with the access token) and hands the file to the browser. */
  open: async (id: string, inline = false) => {
    const r = await api.get<Blob>(`/documents/${id}/download`, { params: { inline }, responseType: 'blob' })
    const url = URL.createObjectURL(r.data)
    const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(r.headers['content-disposition'] ?? '')?.[1]
    const a = document.createElement('a')
    a.href = url
    if (inline) a.target = '_blank'
    else a.download = name ? decodeURIComponent(name) : 'document'
    a.rel = 'noopener'
    a.click()
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  },
}

// ---------------------------------------------------------------- expenses
export interface ExpenseCategory { id: string; code: string; name: string; requiresReceipt: boolean; maxAmountPerLine: number | null }
export interface ExpenseLineInput { categoryId: string; expenseDate: string; description: string; amount: number; currency?: string | null; exchangeRate?: number | null; receiptDocumentId?: string | null }
export interface ExpenseClaimInput { purpose: string; projectCode?: string | null; costCentreCode?: string | null; lines: ExpenseLineInput[] }
export interface ExpenseLine extends ExpenseLineInput { id: string; lineNo: number; category: string; currency: string; exchangeRate: number }
export interface ExpenseClaim {
  id: string; reference: string; purpose: string; currency: string; totalAmount: number; projectCode: string | null; costCentreCode: string | null; status: RequestStatus
  navStatus: NavSyncStatus; navDocumentNo: string | null; paymentReference: string | null; paidAtUtc: string | null; submittedAtUtc: string | null; createdAtUtc: string
  workflowInstanceId: string | null; employeeName: string; lines: ExpenseLine[]; warnings: string[]
}
export interface ExpenseSummary { id: string; reference: string; purpose: string; currency: string; totalAmount: number; status: RequestStatus; navStatus: NavSyncStatus; createdAtUtc: string; submittedAtUtc: string | null; lineCount: number }
export const expensesApi = {
  categories: () => get<ExpenseCategory[]>('/expenses/categories'),
  mine: (page = 1) => get<Paged<ExpenseSummary>>('/expenses/mine', { page }),
  get: (id: string) => get<ExpenseClaim>(`/expenses/${id}`),
  create: (input: ExpenseClaimInput) => post<ExpenseClaim>('/expenses', input),
  update: (id: string, input: ExpenseClaimInput) => put<ExpenseClaim>(`/expenses/${id}`, input),
  submit: (id: string) => post<ExpenseClaim>(`/expenses/${id}/submit`),
  withdraw: (id: string) => post(`/expenses/${id}/withdraw`),
}

// ---------------------------------------------------------------- HR requests
export interface FormField { name: string; label: string; type: 'text' | 'textarea' | 'number' | 'date' | 'select'; required: boolean; options: string[] | null; maxLength: number | null }
export interface HrRequestType { id: string; code: string; name: string; description: string | null; fields: FormField[]; isSensitive: boolean; slaHours: number | null }
export interface HrRequest {
  id: string; reference: string; typeId: string; type: string; subject: string; fields: Record<string, string | null>; status: RequestStatus; submittedAtUtc: string | null
  dueAtUtc: string | null; resolvedAtUtc: string | null; resolution: string | null; workflowInstanceId: string | null; employeeName: string; resolutionDocumentId: string | null
}
export const requestsApi = {
  types: () => get<HrRequestType[]>('/requests/types'),
  submit: (body: { typeId: string; subject: string; fields: Record<string, string> }) => post<HrRequest>('/requests', body),
  mine: () => get<HrRequest[]>('/requests/mine'),
  cancel: (id: string) => post(`/requests/${id}/cancel`),
  queue: (status?: string, page = 1) => get<Paged<HrRequest>>('/requests', { status, page }),
  resolve: (id: string, resolution: string, documentId?: string) => post(`/requests/${id}/resolve`, { resolution, documentId }),
}

// ---------------------------------------------------------------- attendance
export interface AttendanceRecord { id: string; workDate: string; clockInUtc: string | null; clockOutUtc: string | null; lateMinutes: number; workedHours: number; source: string; employeeName: string | null }
export interface AttendanceRequest { id: string; reference: string; type: 'Correction' | 'MissingPunch' | 'Overtime'; workDate: string; requestedClockInUtc: string | null; requestedClockOutUtc: string | null; overtimeHours: number | null; reason: string; status: RequestStatus; workflowInstanceId: string | null; createdAtUtc: string }
export const attendanceApi = {
  today: () => get<{ today: AttendanceRecord | null; shiftName: string; shiftStart: string; shiftEnd: string }>('/attendance/today'),
  clock: (action: 'in' | 'out', coords?: { latitude: number; longitude: number }) => post<AttendanceRecord>('/attendance/clock', { action, ...coords }),
  mine: (from: string, to: string) => get<AttendanceRecord[]>('/attendance/mine', { from, to }),
  team: (date?: string) => get<AttendanceRecord[]>('/attendance/team', { date }),
  request: (body: { type: string; workDate: string; clockIn?: string | null; clockOut?: string | null; overtimeHours?: number | null; reason: string }) => post<AttendanceRequest>('/attendance/requests', body),
  myRequests: () => get<AttendanceRequest[]>('/attendance/requests/mine'),
}

// ---------------------------------------------------------------- training
export interface TrainingProgram { id: string; code: string; title: string; description: string | null; provider: string | null; startsAtUtc: string | null; endsAtUtc: string | null; venue: string | null; capacity: number | null; taken: number; costPerParticipant: number | null; requiresApproval: boolean; isPublished: boolean; myStatus: string | null }
export interface TrainingParticipation { id: string; reference: string; programId: string; program: string; startsAtUtc: string | null; endsAtUtc: string | null; status: string; feedbackRating: number | null; feedbackComment: string | null; employeeName: string | null; workflowInstanceId: string | null }
export type TrainingProgramInput = Omit<TrainingProgram, 'id' | 'taken' | 'myStatus'>
export const trainingApi = {
  programs: (includePast = false) => get<TrainingProgram[]>('/training/programs', { includePast }),
  create: (body: TrainingProgramInput) => post<{ id: string }>('/training/programs', body),
  update: (id: string, body: TrainingProgramInput) => put(`/training/programs/${id}`, body),
  apply: (id: string) => post(`/training/programs/${id}/apply`),
  mine: () => get<TrainingParticipation[]>('/training/mine'),
  withdraw: (id: string) => post(`/training/${id}/withdraw`),
  feedback: (id: string, rating: number, comment?: string) => post(`/training/${id}/feedback`, { rating, comment }),
  participants: (programId: string) => get<TrainingParticipation[]>(`/training/programs/${programId}/participants`),
  setStatus: (id: string, status: string) => post(`/training/${id}/status`, { status }),
}

// ---------------------------------------------------------------- workflow admin
export interface WorkflowStep {
  sequence: number; name: string; approverType: 'LineManager' | 'DepartmentHead' | 'Role' | 'SpecificUser' | 'ManagerOfManager'; approverRole: string | null; approverUserId: string | null
  mode: 'AnyOne' | 'All'; minAmount: number | null; maxAmount: number | null; deadlineHours: number | null; reminderEveryHours: number | null; escalateToRole: string | null; skipIfApproverIsRequester: boolean
}
export interface WorkflowRoute { id: string | null; documentType: string; name: string; owner: 'Portal' | 'Nav'; version: number; isDefault: boolean; runningCount: number; steps: WorkflowStep[] }
export const workflowAdminApi = {
  list: () => get<WorkflowRoute[]>('/admin/workflows'),
  roles: () => get<string[]>('/admin/workflows/roles'),
  save: (documentType: string, body: { name: string; owner: string; steps: WorkflowStep[] }) => put(`/admin/workflows/${encodeURIComponent(documentType)}`, body),
}

// ---------------------------------------------------------------- shared labels
export const statusLabel: Record<string, string> = {
  Draft: 'Draft', Submitted: 'Submitted', InApproval: 'Waiting for approval', ReturnedForCorrection: 'Returned to you', Approved: 'Approved',
  Rejected: 'Rejected', Cancelled: 'Cancelled', Withdrawn: 'Withdrawn', Completed: 'Completed',
  Applied: 'Waiting for approval', Registered: 'Registered', Attended: 'Attended', NoShow: 'Did not attend',
}
export const statusColor = (s: string): 'default' | 'success' | 'warning' | 'error' | 'info' =>
  ['Approved', 'Completed', 'Registered', 'Attended'].includes(s) ? 'success'
    : ['Rejected', 'NoShow'].includes(s) ? 'error'
      : ['ReturnedForCorrection'].includes(s) ? 'warning'
        : ['InApproval', 'Submitted', 'Applied'].includes(s) ? 'info' : 'default'
export const navLabel: Record<NavSyncStatus, string> = {
  NotRequired: '', PendingIntegration: 'Waiting to post to NAV', InProgress: 'Posting to NAV', Posted: 'Recorded in NAV', Failed: 'NAV did not accept it', RequiresReconciliation: 'Being checked with NAV',
}
export const documentTypeLabel = (t: string) => ({ Leave: 'Leave', Expense: 'Expense claim', ProfileChange: 'Personal details', Attendance: 'Attendance', Training: 'Training' } as Record<string, string>)[t] ?? (t.startsWith('HrRequest') ? 'HR request' : t)
export const money = (n: number, currency = 'KES') => `${currency} ${n.toLocaleString('en-KE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
