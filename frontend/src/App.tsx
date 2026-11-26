import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider, Permissions } from './auth/AuthContext'
import { RequireAuth } from './auth/RequireAuth'
import { AppThemeProvider } from './theme/AppThemeProvider'
import { PortalLayout } from './layouts/PortalLayout'
import { LoginPage } from './pages/LoginPage'
import { HomePage } from './pages/HomePage'
import { IntegrationDashboardPage } from './pages/admin/IntegrationDashboardPage'
import { NotFoundPage } from './pages/NotFoundPage'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000, refetchOnWindowFocus: false } },
})

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppThemeProvider>
        <AuthProvider>
          <BrowserRouter>
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route element={<RequireAuth><PortalLayout /></RequireAuth>}>
                <Route index element={<HomePage />} />
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
