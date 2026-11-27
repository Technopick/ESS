import { lazy, Suspense, useEffect } from 'react'
import { Box, CircularProgress } from '@mui/material'
import { BrowserRouter, Route, Routes, useNavigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider, Permissions, reportPermissions } from './auth/AuthContext'
import { RequireAuth } from './auth/RequireAuth'
import { AppThemeProvider } from './theme/AppThemeProvider'
import { PortalLayout } from './layouts/PortalLayout'
import { accountGate } from './api/client'
import { LoginPage } from './pages/LoginPage'
import { ForgotPasswordPage, ResetPasswordPage } from './pages/PasswordPages'
import { DashboardPage } from './pages/DashboardPage'
import { ProfilePage } from './pages/ProfilePage'
import { DirectoryPage } from './pages/DirectoryPage'
import { SecurityPage } from './pages/SecurityPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { LeavePage } from './pages/LeavePage'
import { ApprovalsPage } from './pages/ApprovalsPage'
import { PayslipsPage, PayrollPublishPage } from './pages/PayslipsPage'
import { ExpensesPage } from './pages/ExpensesPage'
import { DocumentsPage } from './pages/DocumentsPage'
import { RequestsPage } from './pages/RequestsPage'
import { AttendancePage } from './pages/AttendancePage'
import { TrainingPage } from './pages/TrainingPage'

/* Administration pages load on demand, so staff on phones download less. */
const UsersPage = lazy(() => import('./pages/admin/UsersPage').then((m) => ({ default: m.UsersPage })))
const RolesPage = lazy(() => import('./pages/admin/RolesPage').then((m) => ({ default: m.RolesPage })))
const IntegrationDashboardPage = lazy(() => import('./pages/admin/IntegrationDashboardPage').then((m) => ({ default: m.IntegrationDashboardPage })))
const WorkflowsPage = lazy(() => import('./pages/admin/WorkflowsPage').then((m) => ({ default: m.WorkflowsPage })))
const NavSettingsPage = lazy(() => import('./pages/admin/NavSettingsPage').then((m) => ({ default: m.NavSettingsPage })))
const NotificationsAdminPage = lazy(() => import('./pages/admin/NotificationsAdminPage').then((m) => ({ default: m.NotificationsAdminPage })))
const ManagementDashboardPage = lazy(() => import('./pages/ManagementDashboardPage').then((m) => ({ default: m.ManagementDashboardPage })))
const ReportsPage = lazy(() => import('./pages/ReportsPage').then((m) => ({ default: m.ReportsPage })))
const HrEmployeesPage = lazy(() => import('./pages/hr/HrEmployeesPage').then((m) => ({ default: m.HrEmployeesPage })))
const HrEmployeeDetailPage = lazy(() => import('./pages/hr/HrEmployeesPage').then((m) => ({ default: m.HrEmployeeDetailPage })))
const OrganizationPage = lazy(() => import('./pages/hr/OrganizationPage').then((m) => ({ default: m.OrganizationPage })))
const LeaveAdminPage = lazy(() => import('./pages/hr/LeaveAdminPage').then((m) => ({ default: m.LeaveAdminPage })))
const HrDocumentsPage = lazy(() => import('./pages/hr/HrDocumentsPage').then((m) => ({ default: m.HrDocumentsPage })))
const AnnouncementsAdminPage = lazy(() => import('./pages/hr/AnnouncementsAdminPage').then((m) => ({ default: m.AnnouncementsAdminPage })))

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000, refetchOnWindowFocus: false } },
})

/** When the API says the account must enrol in MFA or change its password first, send the user to the security page. */
function AccountGateRedirect() {
  const navigate = useNavigate()
  useEffect(() => {
    accountGate.onRequired = () => navigate('/account/security', { replace: true })
    return () => { accountGate.onRequired = () => {} }
  }, [navigate])
  return null
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppThemeProvider>
        <AuthProvider>
          <BrowserRouter>
            <AccountGateRedirect />
            <Suspense fallback={<Box sx={{ display: 'flex', justifyContent: 'center', p: 6 }}><CircularProgress aria-label="Loading" /></Box>}>
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route path="/forgot-password" element={<ForgotPasswordPage />} />
              <Route path="/reset-password" element={<ResetPasswordPage />} />
              <Route element={<RequireAuth><PortalLayout /></RequireAuth>}>
                <Route index element={<DashboardPage />} />
                <Route path="profile" element={<RequireAuth permission={Permissions.ProfileViewOwn}><ProfilePage /></RequireAuth>} />
                <Route path="directory" element={<RequireAuth permission={Permissions.EmployeesViewDirectory}><DirectoryPage /></RequireAuth>} />
                <Route path="account/security" element={<SecurityPage />} />
                <Route path="leave" element={<RequireAuth permission={Permissions.LeaveViewOwn}><LeavePage /></RequireAuth>} />
                <Route path="leave/:id" element={<RequireAuth permission={Permissions.LeaveViewOwn}><LeavePage /></RequireAuth>} />
                <Route path="approvals" element={<RequireAuth permission={Permissions.ApprovalsAct}><ApprovalsPage /></RequireAuth>} />
                <Route path="payslips" element={<RequireAuth permission={Permissions.PayrollViewOwn}><PayslipsPage /></RequireAuth>} />
                <Route path="expenses" element={<RequireAuth permission={Permissions.ExpenseSubmit}><ExpensesPage /></RequireAuth>} />
                <Route path="expenses/:id" element={<RequireAuth permission={Permissions.ExpenseSubmit}><ExpensesPage /></RequireAuth>} />
                <Route path="documents" element={<RequireAuth permission={Permissions.DocumentsViewOwn}><DocumentsPage /></RequireAuth>} />
                <Route path="requests" element={<RequireAuth permission={Permissions.RequestsSubmit}><RequestsPage /></RequireAuth>} />
                <Route path="requests/:id" element={<RequireAuth permission={Permissions.RequestsSubmit}><RequestsPage /></RequireAuth>} />
                <Route path="attendance" element={<RequireAuth permission={Permissions.AttendanceClock}><AttendancePage /></RequireAuth>} />
                <Route path="training" element={<RequireAuth permission={Permissions.TrainingApply}><TrainingPage /></RequireAuth>} />
                <Route path="management" element={<RequireAuth permission={Permissions.DashboardManagement}><ManagementDashboardPage /></RequireAuth>} />
                <Route path="reports" element={<RequireAuth anyOf={reportPermissions}><ReportsPage /></RequireAuth>} />
                <Route path="hr/employees" element={<RequireAuth permission={Permissions.EmployeesViewAll}><HrEmployeesPage /></RequireAuth>} />
                <Route path="hr/employees/:id" element={<RequireAuth permission={Permissions.EmployeesViewAll}><HrEmployeeDetailPage /></RequireAuth>} />
                <Route path="hr/organization" element={<RequireAuth permission={Permissions.EmployeesViewAll}><OrganizationPage /></RequireAuth>} />
                <Route path="hr/leave" element={<RequireAuth permission={Permissions.LeaveViewAll}><LeaveAdminPage /></RequireAuth>} />
                <Route path="hr/documents" element={<RequireAuth permission={Permissions.DocumentsManage}><HrDocumentsPage /></RequireAuth>} />
                <Route path="hr/announcements" element={<RequireAuth permission={Permissions.AnnouncementsManage}><AnnouncementsAdminPage /></RequireAuth>} />
                <Route path="admin/notifications" element={<RequireAuth permission={Permissions.NotificationsConfigure}><NotificationsAdminPage /></RequireAuth>} />
                <Route path="admin/payroll" element={<RequireAuth permission={Permissions.PayrollPublish}><PayrollPublishPage /></RequireAuth>} />
                <Route path="admin/workflows" element={<RequireAuth permission={Permissions.WorkflowConfigure}><WorkflowsPage /></RequireAuth>} />
                <Route path="admin/users" element={<RequireAuth permission={Permissions.UsersManage}><UsersPage /></RequireAuth>} />
                <Route path="admin/roles" element={<RequireAuth permission={Permissions.UsersManage}><RolesPage /></RequireAuth>} />
                <Route path="admin/nav-settings" element={<RequireAuth permission={Permissions.IntegrationConfigure}><NavSettingsPage /></RequireAuth>} />
                <Route path="admin/integration" element={<RequireAuth permission={Permissions.IntegrationView}><IntegrationDashboardPage /></RequireAuth>} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Routes>
            </Suspense>
          </BrowserRouter>
        </AuthProvider>
      </AppThemeProvider>
    </QueryClientProvider>
  )
}
