import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { QRCodeSVG } from 'qrcode.react'
import { Alert, Box, Button, Card, CardContent, CardHeader, Chip, Stack, TextField, Typography } from '@mui/material'
import { accountApi } from '../api/types'
import { errorMessage } from '../api/client'
import { ErrorState, LoadingState } from '../components/StateViews'
import { useAuth } from '../auth/AuthContext'
import { passwordHint } from './PasswordPages'

function RecoveryCodes({ codes }: { codes: string[] }) {
  return (
    <Alert severity="warning" sx={{ mt: 2 }}>
      <Typography fontWeight={600} gutterBottom>Save these recovery codes now</Typography>
      <Typography variant="body2" gutterBottom>Each works once if you lose your phone. They will not be shown again.</Typography>
      <Box component="pre" sx={{ fontFamily: 'monospace', m: 0, columns: 2 }}>{codes.join('\n')}</Box>
      <Button size="small" sx={{ mt: 1 }} onClick={() => navigator.clipboard.writeText(codes.join('\n'))}>Copy codes</Button>
    </Alert>
  )
}

export function SecurityPage() {
  const qc = useQueryClient()
  const { reload } = useAuth()
  const status = useQuery({ queryKey: ['account'], queryFn: accountApi.status })
  const [setup, setSetup] = useState<{ secret: string; otpAuthUri: string } | null>(null)
  const [code, setCode] = useState('')
  const [codes, setCodes] = useState<string[] | null>(null)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [pw, setPw] = useState({ current: '', next: '', confirm: '' })
  const [disable, setDisable] = useState({ password: '', code: '' })

  const refresh = async () => {
    await qc.invalidateQueries({ queryKey: ['account'] })
    await reload()
  }
  const start = useMutation({ mutationFn: accountApi.mfaSetup, onSuccess: setSetup, onError: (e) => setMsg({ ok: false, text: errorMessage(e) }) })
  const confirm = useMutation({
    mutationFn: () => accountApi.mfaConfirm(code),
    onSuccess: async (r) => { setCodes(r.recoveryCodes); setSetup(null); setCode(''); setMsg({ ok: true, text: 'Multi-factor authentication is on.' }); await refresh() },
    onError: (e) => setMsg({ ok: false, text: errorMessage(e) }),
  })
  const off = useMutation({
    mutationFn: () => accountApi.mfaDisable(disable.password, disable.code),
    onSuccess: async () => { setDisable({ password: '', code: '' }); setMsg({ ok: true, text: 'Multi-factor authentication is off.' }); await refresh() },
    onError: (e) => setMsg({ ok: false, text: errorMessage(e) }),
  })
  const changePw = useMutation({
    mutationFn: () => accountApi.changePassword(pw.current, pw.next),
    onSuccess: async () => { setPw({ current: '', next: '', confirm: '' }); setMsg({ ok: true, text: 'Password changed. Other devices have been signed out.' }); await qc.invalidateQueries({ queryKey: ['account'] }) },
    onError: (e) => setMsg({ ok: false, text: errorMessage(e) }),
  })

  if (status.isLoading) return <LoadingState />
  if (status.isError) return <ErrorState error={status.error} onRetry={() => status.refetch()} />
  const s = status.data!

  return (
    <Stack spacing={3} sx={{ maxWidth: 720 }}>
      <Typography variant="h1">Sign-in and security</Typography>
      {s.mustChangePassword && <Alert severity="warning">Change your temporary password to continue using the portal.</Alert>}
      {s.mfaRequiredByPolicy && !s.mfaEnabled && <Alert severity="warning">Your role requires multi-factor authentication. Set it up below to continue.</Alert>}
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} onClose={() => setMsg(null)}>{msg.text}</Alert>}

      <Card>
        <CardHeader title="Authenticator app" action={<Chip color={s.mfaEnabled ? 'success' : 'default'} label={s.mfaEnabled ? 'On' : 'Off'} />}
          subheader="Use Microsoft Authenticator, Google Authenticator or a similar app for a second sign-in step." />
        <CardContent>
          {codes && <RecoveryCodes codes={codes} />}
          {!s.mfaEnabled && !setup && <Button variant="contained" onClick={() => start.mutate()} disabled={start.isPending}>Set up</Button>}
          {setup && (
            <Stack spacing={2} direction={{ xs: 'column', sm: 'row' }} alignItems="flex-start">
              <Box sx={{ p: 1, bgcolor: '#fff', borderRadius: 1 }}><QRCodeSVG value={setup.otpAuthUri} size={168} /></Box>
              <Stack spacing={1.5} sx={{ flex: 1 }}>
                <Typography variant="body2">1. Scan the code with your app, or enter this key: <Box component="code" sx={{ wordBreak: 'break-all' }}>{setup.secret}</Box></Typography>
                <Typography variant="body2">2. Enter the 6-digit code the app shows.</Typography>
                <TextField label="6-digit code" value={code} onChange={(e) => setCode(e.target.value)} inputProps={{ inputMode: 'numeric', maxLength: 6 }} />
                <Stack direction="row" spacing={1}>
                  <Button variant="contained" onClick={() => confirm.mutate()} disabled={confirm.isPending || code.length !== 6}>Turn on</Button>
                  <Button onClick={() => setSetup(null)}>Cancel</Button>
                </Stack>
              </Stack>
            </Stack>
          )}
          {s.mfaEnabled && (
            <Stack spacing={2}>
              <Typography variant="body2">Recovery codes left: {s.recoveryCodesRemaining}</Typography>
              {!s.mfaRequiredByPolicy && (
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                  <TextField size="small" type="password" label="Password" value={disable.password} onChange={(e) => setDisable({ ...disable, password: e.target.value })} />
                  <TextField size="small" label="Code" value={disable.code} onChange={(e) => setDisable({ ...disable, code: e.target.value })} />
                  <Button color="error" onClick={() => off.mutate()} disabled={off.isPending || !disable.password || !disable.code}>Turn off</Button>
                </Stack>
              )}
            </Stack>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader title="Change password" />
        <CardContent>
          <Stack spacing={2} sx={{ maxWidth: 400 }}>
            <TextField type="password" label="Current password" autoComplete="current-password" value={pw.current} onChange={(e) => setPw({ ...pw, current: e.target.value })} />
            <TextField type="password" label="New password" autoComplete="new-password" value={pw.next} onChange={(e) => setPw({ ...pw, next: e.target.value })} helperText={passwordHint} />
            <TextField type="password" label="Confirm new password" autoComplete="new-password" value={pw.confirm} onChange={(e) => setPw({ ...pw, confirm: e.target.value })}
              error={!!pw.confirm && pw.confirm !== pw.next} helperText={pw.confirm && pw.confirm !== pw.next ? 'The passwords do not match.' : ' '} />
            <Button variant="contained" onClick={() => changePw.mutate()} disabled={changePw.isPending || !pw.current || pw.next.length < 12 || pw.next !== pw.confirm}>
              Change password
            </Button>
          </Stack>
        </CardContent>
      </Card>
    </Stack>
  )
}
