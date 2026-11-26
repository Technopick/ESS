import { api } from './client'

export interface EmployeeSummary {
  id: string
  employeeNo: string
  fullName: string
  jobTitle: string | null
  department: string | null
  location: string | null
  managerId: string | null
  managerName: string | null
  status: string
  employmentDate: string | null
  contractEndDate: string | null
  workEmail: string | null
  lastSyncedAtUtc: string
}

export interface LeaveBalance {
  leaveTypeId: string
  leaveTypeCode: string
  leaveType: string
  period: string
  entitlement: number
  broughtForward: number
  taken: number
  pending: number
  available: number
}

export interface Dashboard {
  employee: EmployeeSummary | null
  leave: { balances: LeaveBalance[]; retrievedAtUtc: string | null; fromCache: boolean; navMessage: string | null } | null
  upcomingLeave: { id: string; reference: string; leaveType: string; startDate: string; endDate: string; days: number; status: string }[]
  recentPayslips: { periodId: string; name: string; payDate: string | null }[]
  pending: { myOpenRequests: number; myPendingLeave: number; approvalsWaitingForMe: number; unreadNotifications: number }
  announcements: { id: string; title: string; bodyHtml: string; publishAtUtc: string; isPinned: boolean }[]
  celebrations: { name: string; kind: string; date: string; years: number | null; department: string | null }[]
}

export type Dict = Record<string, string | null>

export interface NavProfile {
  core: Record<string, unknown>
  personalDetails: Dict
  address: Dict
  emergencyContacts: Dict[]
  nextOfKin: Dict[]
  dependants: Dict[]
  qualifications: Dict[]
  bankDetailsMasked: Dict | null
}

export interface ProfileResponse {
  summary: EmployeeSummary
  nav: NavProfile | null
  navAvailable: boolean
  navMessage: string | null
}

export interface ChangeRequest {
  id: string
  reference: string
  section: string
  changes: Dict | null
  status: string
  navStatus: string
  createdAtUtc: string
}

export interface DirectoryEntry {
  id: string
  fullName: string
  jobTitle: string | null
  department: string | null
  location: string | null
  workEmail: string | null
  employeeNo: string | null
  status: string | null
  managerName: string | null
  employmentDate: string | null
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface Lookup {
  id: string
  code: string
  name: string
}

export interface UserView {
  id: string
  userName: string
  email: string
  isActive: boolean
  mfaEnabled: boolean
  isLocked: boolean
  hasPassword: boolean
  lastLoginAtUtc: string | null
  roles: { roleId: string; role: string; companyId: string | null; scopeDepartmentId: string | null; scopeDepartment: string | null }[]
  employees: { companyId: string; company: string; employeeId: string; employeeNo: string; employeeName: string }[]
  defaultCompanyId: string | null
}

export interface RoleView {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  userCount: number
  permissions: string[]
}

export interface PermissionView {
  code: string
  module: string
  description: string | null
}

export interface SecurityStatus {
  userName: string
  email: string
  mfaEnabled: boolean
  recoveryCodesRemaining: number
  mfaRequiredByPolicy: boolean
  mustChangePassword: boolean
  passwordChangedAtUtc: string | null
  lastLoginAtUtc: string | null
}

export const essApi = {
  dashboard: () => api.get<Dashboard>('/dashboard/me').then((r) => r.data),
  profile: () => api.get<ProfileResponse>('/employees/me/profile').then((r) => r.data),
  changeRequests: () => api.get<ChangeRequest[]>('/employees/me/change-requests').then((r) => r.data),
  requestChange: (section: string, changes: Dict, reason?: string) =>
    api.post<{ id: string; reference: string }>('/employees/me/change-requests', { section, changes, reason }).then((r) => r.data),
  directory: (params: { search?: string; departmentId?: string; page?: number }) =>
    api.get<Paged<DirectoryEntry>>('/employees', { params: { ...params, pageSize: 25 } }).then((r) => r.data),
  companies: () => api.get<Lookup[]>('/lookups/companies').then((r) => r.data),
  departments: (companyId: string) => api.get<Lookup[]>('/lookups/departments', { params: { companyId } }).then((r) => r.data),
}

export const accountApi = {
  status: () => api.get<SecurityStatus>('/account').then((r) => r.data),
  changePassword: (currentPassword: string, newPassword: string) => api.post('/account/change-password', { currentPassword, newPassword }),
  mfaSetup: () => api.post<{ secret: string; otpAuthUri: string }>('/account/mfa/setup').then((r) => r.data),
  mfaConfirm: (code: string) => api.post<{ recoveryCodes: string[] }>('/account/mfa/confirm', { code }).then((r) => r.data),
  mfaDisable: (password: string, code: string) => api.post('/account/mfa/disable', { password, code }),
  regenerateCodes: (code: string) => api.post<{ recoveryCodes: string[] }>('/account/mfa/recovery-codes', { code }).then((r) => r.data),
  forgot: (email: string) => api.post('/auth/forgot-password', { email }),
  reset: (token: string, newPassword: string) => api.post('/auth/reset-password', { token, newPassword }),
}

export interface RoleAssignmentInput {
  roleId: string
  companyId?: string | null
  scopeDepartmentId?: string | null
}

export const adminApi = {
  users: (params: { search?: string; roleId?: string; page?: number }) =>
    api.get<Paged<UserView>>('/admin/users', { params: { ...params, pageSize: 25 } }).then((r) => r.data),
  createUser: (body: { userName: string; email: string; roles: RoleAssignmentInput[]; companyId?: string | null; employeeNo?: string | null; sendInvite: boolean }) =>
    api.post<UserView>('/admin/users', body).then((r) => r.data),
  setRoles: (id: string, roles: RoleAssignmentInput[]) => api.put(`/admin/users/${id}/roles`, { roles }),
  linkEmployee: (id: string, companyId: string, employeeNo: string) => api.post(`/admin/users/${id}/employee-links`, { companyId, employeeNo }),
  unlinkEmployee: (id: string, companyId: string) => api.delete(`/admin/users/${id}/employee-links/${companyId}`),
  action: (id: string, action: 'disable' | 'enable' | 'unlock' | 'reset-mfa' | 'send-password-link') => api.post(`/admin/users/${id}/${action}`),
  provision: (companyId: string, sendInvites: boolean, dryRun: boolean) =>
    api
      .post<{ eligible: number; created: number; linkedExisting: number; skippedNoEmail: number; skippedConflict: number; conflicts: string[] }>(
        '/admin/users/provision',
        { companyId, sendInvites, dryRun },
      )
      .then((r) => r.data),
  roles: () => api.get<RoleView[]>('/admin/roles').then((r) => r.data),
  permissions: () => api.get<PermissionView[]>('/admin/permissions').then((r) => r.data),
  createRole: (name: string, description: string | null, permissions: string[]) => api.post('/admin/roles', { name, description, permissions }),
  updateRole: (id: string, name: string, description: string | null, permissions: string[]) =>
    api.put(`/admin/roles/${id}`, { name, description, permissions }),
  deleteRole: (id: string) => api.delete(`/admin/roles/${id}`),
}
