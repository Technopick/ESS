import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Alert, Box, Button, Card, CardContent, Stack, TextField, Typography } from '@mui/material'
import { useAuth } from '../auth/AuthContext'
import { errorMessage } from '../api/client'
import { useThemeMode } from '../theme/AppThemeProvider'

const schema = z.object({
  userName: z.string().trim().min(1, 'Enter your user name or email'),
  password: z.string().min(1, 'Enter your password'),
})
type FormValues = z.infer<typeof schema>

export function LoginPage() {
  const { me, login } = useAuth()
  const { branding } = useThemeMode()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const { register, handleSubmit, formState } = useForm<FormValues>({ resolver: zodResolver(schema) })

  if (me) return <Navigate to="/" replace />

  const onSubmit = async (v: FormValues) => {
    setError(null)
    try {
      await login(v.userName, v.password)
      navigate((location.state as { from?: string } | null)?.from ?? '/', { replace: true })
    } catch (e) {
      setError(errorMessage(e))
    }
  }

  return (
    <Box className="flex min-h-full items-center justify-center p-4" sx={{ bgcolor: 'background.default' }}>
      <Card sx={{ width: '100%', maxWidth: 400 }}>
        <CardContent sx={{ p: 4 }}>
          <Typography variant="h1" gutterBottom>
            {branding.companyName}
          </Typography>
          <Typography color="text.secondary" sx={{ mb: 3 }}>
            Sign in to continue
          </Typography>
          <form onSubmit={handleSubmit(onSubmit)} noValidate>
            <Stack spacing={2}>
              {error && <Alert severity="error">{error}</Alert>}
              <TextField label="User name or email" autoComplete="username" autoFocus {...register('userName')}
                error={!!formState.errors.userName} helperText={formState.errors.userName?.message} />
              <TextField label="Password" type="password" autoComplete="current-password" {...register('password')}
                error={!!formState.errors.password} helperText={formState.errors.password?.message} />
              <Button type="submit" variant="contained" size="large" disabled={formState.isSubmitting}>
                {formState.isSubmitting ? 'Signing in…' : 'Sign in'}
              </Button>
            </Stack>
          </form>
        </CardContent>
      </Card>
    </Box>
  )
}
