import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link as RouterLink, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Alert, Box, Button, Link, Stack, TextField, Typography, useTheme } from '@mui/material'
import { useAuth } from '../auth/AuthContext'
import { errorMessage } from '../api/client'
import { useThemeMode } from '../theme/AppThemeProvider'
import { brandGradient } from '../theme/buildTheme'
import { BrandMark } from '../components/BrandMark'

const schema = z.object({
  userName: z.string().trim().min(1, 'Enter your user name or email'),
  password: z.string().min(1, 'Enter your password'),
})
type FormValues = z.infer<typeof schema>

export function LoginPage() {
  const { me, login, verifyMfa } = useAuth()
  const { branding } = useThemeMode()
  const theme = useTheme()
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
    <Box className="flex min-h-full" sx={{ bgcolor: 'background.default' }}>
      <Box sx={{ display: { xs: 'none', md: 'flex' }, flexDirection: 'column', justifyContent: 'space-between', width: '44%', maxWidth: 620, p: 6,
        color: '#fff', background: brandGradient(theme), position: 'relative', overflow: 'hidden' }}>
        <Box aria-hidden sx={{ position: 'absolute', width: 520, height: 520, borderRadius: '50%', right: -180, bottom: -200, bgcolor: 'rgba(255,255,255,0.07)' }} />
        <Box aria-hidden sx={{ position: 'absolute', width: 260, height: 260, borderRadius: '50%', right: 120, top: -90, bgcolor: 'rgba(255,255,255,0.05)' }} />
        <BrandMark size={44} inverse showName={false} />
        <Box sx={{ position: 'relative' }}>
          <Typography component="p" sx={{ fontSize: '2.2rem', fontWeight: 700, lineHeight: 1.15, mb: 2 }}>{branding.appName}</Typography>
          <Typography sx={{ fontSize: '1.1rem', opacity: 0.9, maxWidth: 420 }}>
            {branding.welcomeText ?? 'Leave, payslips, claims and HR requests in one place.'}
          </Typography>
        </Box>
        <Typography variant="body2" sx={{ opacity: 0.75, position: 'relative' }}>Signed-in activity is recorded to protect your information.</Typography>
      </Box>
      <Box className="flex flex-1 items-center justify-center p-4">
        <Box sx={{ width: '100%', maxWidth: 400 }}>
          <Box sx={{ display: { md: 'none' }, mb: 3 }}><BrandMark size={40} /></Box>
          <Typography variant="h1" gutterBottom>{challenge ? 'Two-step verification' : 'Welcome back'}</Typography>
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
        </Box>
      </Box>
    </Box>
  )
}
