import { useEffect } from 'react'
import { BrowserRouter, Route, Routes, useNavigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider, Permissions } from './auth/AuthContext'
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
import { UsersPage } from './pages/admin/UsersPage'
import { RolesPage } from './pages/admin/RolesPage'
import { IntegrationDashboardPage } from './pages/admin/IntegrationDashboardPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { LeavePage } from './pages/LeavePage'
import { ApprovalsPage } from './pages/ApprovalsPage'
import { PayslipsPage, PayrollPublishPage } from './pages/PayslipsPage'
import { ExpensesPage } from './pages/ExpensesPage'
import { DocumentsPage } from './pages/DocumentsPage'
import { RequestsPage } from './pages/RequestsPage'
import { AttendancePage } from './pages/AttendancePage'
import { TrainingPage } from './pages/TrainingPage'
import { WorkflowsPage } from './pages/admin/WorkflowsPage'

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
                <Route path="admin/payroll" element={<RequireAuth permission={Permissions.PayrollPublish}><PayrollPublishPage /></RequireAuth>} />
                <Route path="admin/workflows" element={<RequireAuth permission={Permissions.WorkflowConfigure}><WorkflowsPage /></RequireAuth>} />
                <Route path="admin/users" element={<RequireAuth permission={Permissions.UsersManage}><UsersPage /></RequireAuth>} />
                <Route path="admin/roles" element={<RequireAuth permission={Permissions.UsersManage}><RolesPage /></RequireAuth>} />
                <Route path="admin/integration" element={<RequireAuth permission={Permissions.IntegrationView}><IntegrationDashboardPage /></RequireAuth>} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </AuthProvider>
      </AppThemeProvider>
    </QueryClientProvider>
  )
}
