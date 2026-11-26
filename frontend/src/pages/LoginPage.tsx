import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link as RouterLink, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Alert, Box, Button, Card, CardContent, Link, Stack, TextField, Typography } from '@mui/material'
import { useAuth } from '../auth/AuthContext'
import { errorMessage } from '../api/client'
import { useThemeMode } from '../theme/AppThemeProvider'

const schema = z.object({
  userName: z.string().trim().min(1, 'Enter your user name or email'),
  password: z.string().min(1, 'Enter your password'),
})
type FormValues = z.infer<typeof schema>

export function LoginPage() {
  const { me, login, verifyMfa } = useAuth()
  const { branding } = useThemeMode()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const [challenge, setChallenge] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const { register, handleSubmit, formState } = useForm<FormValues>({ resolver: zodResolver(schema) })

  if (me) return <Navigate to="/" replace />
  const goOn = () => navigate((location.state as { from?: string } | null)?.from ?? '/', { replace: true })

  const onSubmit = async (v: FormValues) => {
    setError(null)
    try {
      const r = await login(v.userName, v.password)
      if (r.done) goOn()
      else setChallenge(r.mfaChallenge)
    } catch (e) {
      setError(errorMessage(e))
    }
  }

  const onVerify = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!challenge) return
    setBusy(true)
    setError(null)
    try {
      await verifyMfa(challenge, code)
      goOn()
    } catch (err) {
      setError(errorMessage(err))
      if (/expired/i.test(errorMessage(err))) setChallenge(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Box className="flex min-h-full items-center justify-center p-4" sx={{ bgcolor: 'background.default' }}>
      <Card sx={{ width: '100%', maxWidth: 400 }}>
        <CardContent sx={{ p: 4 }}>
          <Typography variant="h1" gutterBottom>
            {branding.companyName}
          </Typography>
          {!challenge ? (
            <>
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
                  <Link component={RouterLink} to="/forgot-password" variant="body2" sx={{ alignSelf: 'center' }}>
                    Forgot your password?
                  </Link>
                </Stack>
              </form>
            </>
          ) : (
            <form onSubmit={onVerify} noValidate>
              <Stack spacing={2}>
                <Typography color="text.secondary">
                  Enter the 6-digit code from your authenticator app, or one of your recovery codes.
                </Typography>
                {error && <Alert severity="error">{error}</Alert>}
                <TextField label="Verification code" autoComplete="one-time-code" inputProps={{ inputMode: 'text', maxLength: 11 }} autoFocus
                  value={code} onChange={(e) => setCode(e.target.value)} />
                <Button type="submit" variant="contained" size="large" disabled={busy || code.trim().length < 6}>
                  {busy ? 'Checking…' : 'Verify'}
                </Button>
                <Button onClick={() => { setChallenge(null); setCode(''); setError(null) }}>Back</Button>
              </Stack>
            </form>
          )}
        </CardContent>
      </Card>
    </Box>
  )
}
