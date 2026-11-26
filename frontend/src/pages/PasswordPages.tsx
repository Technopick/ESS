import { useState } from 'react'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom'
import { Alert, Box, Button, Card, CardContent, Link, Stack, TextField, Typography } from '@mui/material'
import { accountApi } from '../api/types'
import { errorMessage } from '../api/client'

function Shell({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box className="flex min-h-full items-center justify-center p-4">
      <Card sx={{ width: '100%', maxWidth: 420 }}>
        <CardContent sx={{ p: 4 }}>
          <Typography variant="h1" gutterBottom>{title}</Typography>
          {children}
        </CardContent>
      </Card>
    </Box>
  )
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  return (
    <Shell title="Reset your password">
      {sent ? (
        <Stack spacing={2}>
          <Alert severity="success">If an account exists for {email}, we've emailed a link to set a new password. It expires in 30 minutes.</Alert>
          <Link component={RouterLink} to="/login">Back to sign in</Link>
        </Stack>
      ) : (
        <form onSubmit={async (e) => {
          e.preventDefault()
          setBusy(true)
          setError(null)
          try { await accountApi.forgot(email); setSent(true) } catch (err) { setError(errorMessage(err)) } finally { setBusy(false) }
        }}>
          <Stack spacing={2}>
            <Typography color="text.secondary">Enter your work email and we'll send you a reset link.</Typography>
            {error && <Alert severity="error">{error}</Alert>}
            <TextField label="Work email" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />
            <Button type="submit" variant="contained" disabled={busy || !email.includes('@')}>Send link</Button>
            <Link component={RouterLink} to="/login" variant="body2">Back to sign in</Link>
          </Stack>
        </form>
      )}
    </Shell>
  )
}

export const passwordHint = 'At least 12 characters, using three of: lower-case, upper-case, digits, symbols.'

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const navigate = useNavigate()
  const [pw, setPw] = useState('')
  const [pw2, setPw2] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)
  if (!token) return <Shell title="Set your password"><Alert severity="error">This link is incomplete. Open it again from your email.</Alert></Shell>
  return (
    <Shell title="Set your password">
      {done ? (
        <Stack spacing={2}>
          <Alert severity="success">Your password is set. You can sign in now.</Alert>
          <Button variant="contained" onClick={() => navigate('/login')}>Sign in</Button>
        </Stack>
      ) : (
        <form onSubmit={async (e) => {
          e.preventDefault()
          if (pw !== pw2) { setError('The passwords do not match.'); return }
          setBusy(true)
          setError(null)
          try { await accountApi.reset(token, pw); setDone(true) } catch (err) { setError(errorMessage(err)) } finally { setBusy(false) }
        }}>
          <Stack spacing={2}>
            {error && <Alert severity="error">{error}</Alert>}
            <TextField label="New password" type="password" autoComplete="new-password" value={pw} onChange={(e) => setPw(e.target.value)} helperText={passwordHint} autoFocus />
            <TextField label="Confirm new password" type="password" autoComplete="new-password" value={pw2} onChange={(e) => setPw2(e.target.value)} />
            <Button type="submit" variant="contained" disabled={busy || pw.length < 12}>Set password</Button>
          </Stack>
        </form>
      )}
    </Shell>
  )
}
