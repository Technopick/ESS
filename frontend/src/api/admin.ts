import { api } from './client'
import type { NavSyncStatus, Paged, RequestStatus } from './modules'

const get = <T,>(url: string, params?: object) => api.get<T>(url, { params }).then((r) => r.data)
const post = <T = void,>(url: string, body?: unknown) => api.post<T>(url, body ?? {}).then((r) => r.data)
const put = <T = void,>(url: string, body?: unknown) => api.put<T>(url, body).then((r) => r.data)
const del = (url: string) => api.delete(url).then(() => undefined)

/** Drops empty values so the API never receives "undefined" or blank filters. */
export const clean = (o: object) => Object.fromEntries(Object.entries(o).filter(([, v]) => v !== '' && v !== null && v !== undefined))

/** Downloads a file response (exports) and saves it with the server's file name. */
export async function download(url: string, params: object) {
  const r = await api.get<Blob>(url, { params: clean(params), responseType: 'blob' })
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(r.headers['content-disposition'] ?? '')?.[1]
  const href = URL.createObjectURL(r.data)
  const a = document.createElement('a')
  a.href = href
  a.download = name ? decodeURIComponent(name) : 'export'
  a.rel = 'noopener'
  a.click()
  setTimeout(() => URL.revokeObjectURL(href), 60_000)
}

export type ExportFormat = 'Csv' | 'Xlsx' | 'Pdf'
export interface Item { id: string; code: string; name: string }
export const lookupsApi = {
  departments: (companyId: string) => get<Item[]>('/lookups/departments', { companyId }),
  locations: (companyId: string) => get<Item[]>('/lookups/locations', { companyId }),
}

// ---------------------------------------------------------------- HR employees
export type EmploymentStatus = 'Active' | 'Inactive' | 'Terminated' | 'OnProbation' | 'Suspended'
export interface EmployeeRow {
  id: string; employeeNo: string; name: string; position: string | null; department: string | null; location: string | null; status: EmploymentStatus
  manager: string | null; employmentDate: string | null; contractEndDate: string | null; hasAccount: boolean; lastSyncedAtUtc: string
}
export interface EmployeeDetail {
  employee: EmployeeRow; gender: string | null; terminationDate: string | null; workEmail: string | null; mobilePhone: string | null; managerId: string | null
  account: { userId: string; userName: string; email: string; isActive: boolean; mfaEnabled: boolean; lastLoginAtUtc: string | null; roles: string[] } | null
  directReports: { id: string; name: string; position: string | null }[]
  balances: { leaveType: string; period: string; entitlement: number; taken: number; pending: number; available: number; retrievedAtUtc: string }[]
  activity: { id: string; kind: string; reference: string; summary: string; status: string; atUtc: string | null }[]
  documents: number; expiringDocuments: number; pendingProfileChanges: number
}
export interface NavProfile {
  profile: {
    personalDetails: Record<string, string | null>; address: Record<string, string | null>; emergencyContacts: Record<string, string | null>[]
    nextOfKin: Record<string, string | null>[]; dependants: Record<string, string | null>[]; qualifications: Record<string, string | null>[]; bankDetailsMasked: Record<string, string | null> | null
  } | null
  available: boolean; message: string | null
}
export interface EmployeeQuery { search?: string; departmentId?: string; locationId?: string; status?: string; contractEndingSoon?: boolean; withoutAccount?: boolean; sort?: string; page?: number }
export const hrEmployeesApi = {
  list: (q: EmployeeQuery) => get<Paged<EmployeeRow>>('/hr/employees', clean({ ...q, pageSize: 25 })),
  get: (id: string) => get<EmployeeDetail>(`/hr/employees/${id}`),
  profile: (id: string) => get<NavProfile>(`/hr/employees/${id}/profile`),
  refresh: (id: string) => post(`/hr/employees/${id}/refresh`),
}

// ---------------------------------------------------------------- organisation
export interface DepartmentRow { id: string; code: string; name: string; parentId: string | null; parent: string | null; headEmployeeId: string | null; head: string | null; headcount: number; lastSyncedAtUtc: string }
export interface OrgRow { id: string; code: string; name: string; department: string | null; headcount: number; lastSyncedAtUtc: string }
export const organizationApi = {
  departments: () => get<DepartmentRow[]>('/hr/organization/departments'),
  positions: () => get<OrgRow[]>('/hr/organization/positions'),
  locations: () => get<OrgRow[]>('/hr/organization/locations'),
  setRouting: (id: string, body: { headEmployeeId: string | null; parentDepartmentId: string | null }) => put(`/hr/organization/departments/${id}`, body),
}

// ---------------------------------------------------------------- leave administration
export interface LeaveApplicationRow {
  id: string; reference: string; employeeId: string; employee: string; department: string | null; leaveType: string; startDate: string; endDate: string; days: number
  status: RequestStatus; navStatus: NavSyncStatus; navDocumentNo: string | null; submittedAtUtc: string | null; workflowInstanceId: string | null; pendingWith: string | null
}
export interface LeaveTypeRules { countsWorkingDaysOnly: boolean; requiresAttachment: boolean; maxDaysPerApplication: number | null; minNoticeDays: number; allowNegativeBalance: boolean; allowHalfDay: boolean; eligibleGender: string | null; isActive: boolean }
export interface LeaveTypeRow extends LeaveTypeRules { id: string; code: string; name: string; lastSyncedAtUtc: string }
export interface Holiday { id: string; date: string; name: string; fromNav: boolean }
export const leaveAdminApi = {
  applications: (q: { status?: string; leaveTypeId?: string; departmentId?: string; from?: string; to?: string; search?: string; navStatus?: string; page?: number }) =>
    get<Paged<LeaveApplicationRow>>('/hr/leave/applications', clean({ ...q, pageSize: 25 })),
  types: () => get<LeaveTypeRow[]>('/hr/leave/types'),
  updateType: (id: string, rules: LeaveTypeRules) => put(`/hr/leave/types/${id}`, rules),
  holidays: (year: number) => get<Holiday[]>('/hr/leave/holidays', { year }),
  addHoliday: (date: string, name: string) => post<Holiday>('/hr/leave/holidays', { date, name }),
  removeHoliday: (id: string) => del(`/hr/leave/holidays/${id}`),
}

// ---------------------------------------------------------------- announcements
export interface Announcement { id: string; title: string; bodyHtml: string; publishAtUtc: string; expiresAtUtc: string | null; isPinned: boolean }
export interface AnnouncementAdmin extends Announcement {
  targetDepartmentId: string | null; targetDepartment: string | null; targetLocationId: string | null; targetLocation: string | null
  notifyOnPublish: boolean; notifyByEmail: boolean; notifiedAtUtc: string | null; state: 'Live' | 'Scheduled' | 'Expired'
}
export interface AnnouncementInput {
  title: string; body: string; publishAtUtc: string | null; expiresAtUtc: string | null; isPinned: boolean; targetDepartmentId: string | null; targetLocationId: string | null
  notifyOnPublish: boolean; notifyByEmail: boolean
}
export const announcementsApi = {
  feed: () => get<Announcement[]>('/announcements'),
  manage: () => get<AnnouncementAdmin[]>('/announcements/manage'),
  create: (b: AnnouncementInput) => post<{ id: string }>('/announcements', b),
  update: (id: string, b: AnnouncementInput) => put(`/announcements/${id}`, b),
  remove: (id: string) => del(`/announcements/${id}`),
}

// ---------------------------------------------------------------- notification administration
export type Channel = 'InApp' | 'Email' | 'Sms' | 'WhatsApp'
export type DeliveryStatus = 'Pending' | 'Sent' | 'Delivered' | 'Failed' | 'Suppressed'
export interface TemplateView { id: string; channel: Channel; subject: string | null; body: string; isActive: boolean; modifiedAtUtc: string | null }
export interface TemplatesOverview { events: { code: string; description: string; templates: TemplateView[] }[]; placeholders: string[]; smsConfigured: boolean; emailConfigured: boolean }
export interface DeliveryRow { id: string; createdAtUtc: string; eventCode: string; channel: Channel; recipient: string; subject: string | null; status: DeliveryStatus; attempts: number; sentAtUtc: string | null; readAtUtc: string | null; lastError: string | null }
export const notificationAdminApi = {
  templates: () => get<TemplatesOverview>('/admin/notifications/templates'),
  save: (event: string, channel: Channel, b: { subject: string | null; body: string; isActive: boolean }) => put(`/admin/notifications/templates/${encodeURIComponent(event)}/${channel}`, b),
  reset: (event: string, channel: Channel) => del(`/admin/notifications/templates/${encodeURIComponent(event)}/${channel}`),
  preview: (b: { subject: string | null; body: string }) => post<{ subject: string | null; body: string }>('/admin/notifications/templates/preview', b),
  deliveries: (q: { status?: string; channel?: string; eventCode?: string; recipient?: string; page?: number }) => get<Paged<DeliveryRow>>('/admin/notifications/deliveries', clean(q)),
  stats: () => get<{ pending: number; failed: number; sentToday: number; suppressedToday: number }>('/admin/notifications/deliveries/stats'),
  retry: (id?: string) => post<{ queued: number }>(`/admin/notifications/deliveries/retry${id ? `?id=${id}` : ''}`),
}

// ---------------------------------------------------------------- management dashboard
export interface CountItem { label: string; count: number }
export interface MonthPoint { month: string; value: number }
export interface PersonItem { employeeId: string; name: string; department: string | null; date: string; detail: string | null }
export interface ManagementDashboard {
  from: string; to: string; today: string; scopeLabel: string
  totalEmployees: number; activeEmployees: number; onLeaveToday: number; pendingLeave: number; pendingExpenses: number; pendingExpenseAmount: number; openHrRequests: number
  joiners: number; leavers: number; turnoverPercent: number | null
  byDepartment: CountItem[]; byLocation: CountItem[]; byStatus: CountItem[]; byGender: CountItem[]
  onLeave: PersonItem[]; contractExpiries: PersonItem[]; birthdays: PersonItem[]; anniversaries: PersonItem[]
  leaveDaysByMonth: MonthPoint[]; headcountByMonth: MonthPoint[]
  training: { programmes: number; participants: number; completed: number; noShows: number; averageRating: number | null; byStatus: CountItem[] }
  requests: { submitted: number; resolved: number; open: number; withinSlaPercent: number | null; averageHoursToResolve: number | null; byType: CountItem[] }
  birthdaysShown: boolean
}
export interface DashboardQuery { departmentId?: string; locationId?: string; status?: string; from?: string; to?: string }
export const managementApi = {
  dashboard: (q: DashboardQuery) => get<ManagementDashboard>('/management/dashboard', clean(q)),
  export: (q: DashboardQuery, format: ExportFormat) => download('/management/dashboard/export', { ...q, format }),
}

// ---------------------------------------------------------------- reports
export type ColumnType = 'Text' | 'Number' | 'Decimal' | 'Money' | 'Date' | 'DateTime' | 'Percent'
export interface ReportInfo {
  code: string; name: string; category: string; description: string
  filters: { dateRange: boolean; department: boolean; location: boolean; employee: boolean; statuses: string[] | null; period: boolean }
}
export interface ReportTable {
  title: string; columns: { key: string; label: string; type: ColumnType }[]; rows: (string | number | boolean | null)[][]
  filters: { key: string; value: string }[]; note: string | null; truncated: boolean; totals: (string | number | boolean | null)[] | null
}
export interface ReportQuery { from?: string; to?: string; departmentId?: string; locationId?: string; employeeId?: string; status?: string; periodCode?: string }
export const reportsApi = {
  catalog: () => get<{ reports: ReportInfo[]; canExport: boolean }>('/reports'),
  run: (code: string, q: ReportQuery) => get<ReportTable>(`/reports/${code}`, clean(q)),
  export: (code: string, q: ReportQuery, format: ExportFormat) => download(`/reports/${code}/export`, { ...q, format }),
}

export const statusName = (s: string) => ({ OnProbation: 'On probation', InApproval: 'In approval', ReturnedForCorrection: 'Returned', NoShow: 'Did not attend' } as Record<string, string>)[s] ?? s
