import { Box, CircularProgress } from '@mui/material'
import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'
import { ForbiddenState } from '../components/StateViews'

export function RequireAuth({ children, permission }: { children: ReactNode; permission?: string }) {
  const { me, loading, can } = useAuth()
  const location = useLocation()
  if (loading)
    return (
      <Box className="flex h-full items-center justify-center">
        <CircularProgress aria-label="Loading" />
      </Box>
    )
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if (permission && !can(permission)) return <ForbiddenState />
  return <>{children}</>
}
