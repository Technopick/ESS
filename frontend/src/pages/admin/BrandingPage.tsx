import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, Avatar, Box, Button, ButtonBase, Card, CardContent, Chip, Divider, LinearProgress, List, ListItemButton, ListItemIcon, ListItemText, Paper,
  Snackbar, Stack, Switch, TextField, ThemeProvider, ToggleButton, ToggleButtonGroup, Tooltip, Typography, useTheme,
} from '@mui/material'
import HomeOutlinedIcon from '@mui/icons-material/HomeOutlined'
import EventAvailableOutlinedIcon from '@mui/icons-material/EventAvailableOutlined'
import ReceiptLongOutlinedIcon from '@mui/icons-material/ReceiptLongOutlined'
import CheckIcon from '@mui/icons-material/Check'
import { brandingApi, type BrandingSettings, type BrandingView, type DefaultMode } from '../../api/branding'
import { errorMessage } from '../../api/client'
import { ErrorState, LoadingState } from '../../components/StateViews'
import { brandGradient, brandTint, buildTheme, contrast, defaultBranding, forDark, isHex, presets } from '../../theme/buildTheme'

function ColourField({ label, value, onChange, helper }: { label: string; value: string; onChange: (v: string) => void; helper: React.ReactNode }) {
  const [text, setText] = useState(value)
  useEffect(() => setText(value), [value])
  return (
    <Stack direction="row" spacing={1.5} alignItems="flex-start">
      <Box component="input" type="color" aria-label={`${label} picker`} value={isHex(value) ? value : '#000000'} onChange={(e: React.ChangeEvent<HTMLInputElement>) => onChange(e.target.value)}
        sx={{ width: 56, height: 56, p: 0.5, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'background.paper', cursor: 'pointer', flexShrink: 0 }} />
      <TextField fullWidth label={label} value={text} error={!isHex(text)} helperText={isHex(text) ? helper : 'Use #rrggbb, e.g. #0b5394'}
        onChange={(e) => { const v = e.target.value.trim(); setText(v); if (isHex(v)) onChange(v.toLowerCase()) }} />
    </Stack>
  )
}

function ContrastChip({ ratio }: { ratio: number }) {
  const level = ratio >= 7 ? 'AAA' : ratio >= 4.5 ? 'AA' : ratio >= 3 ? 'Large text only' : 'Too low'
  return <Chip size="small" color={ratio >= 4.5 ? 'success' : ratio >= 3 ? 'warning' : 'error'} variant="outlined" label={`${ratio.toFixed(1)}:1 ${level}`} />
}

/** A small portal mock-up in the candidate theme, so changes can be seen before saving. */
function Preview({ b, dark }: { b: BrandingView; dark: boolean }) {
  const theme = useMemo(() => buildTheme(b, dark), [b, dark])
  const initials = b.appName.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('')
  return (
    <ThemeProvider theme={theme}>
      <Paper variant="outlined" sx={{ overflow: 'hidden', bgcolor: 'background.default', color: 'text.primary' }}>
        <Box sx={{ display: 'flex', minHeight: 360 }}>
          <Box sx={{ width: 170, borderRight: 1, borderColor: 'divider', bgcolor: 'background.paper', display: { xs: 'none', sm: 'block' } }}>
            <Stack direction="row" spacing={1} alignItems="center" sx={{ p: 1.5, minHeight: 52 }}>
              {b.logoUrl ? <Box component="img" src={b.logoUrl} alt="" sx={{ height: 26, maxWidth: 110, objectFit: 'contain' }} />
                : <Avatar variant="rounded" sx={{ width: 26, height: 26, fontSize: 12, fontWeight: 700, bgcolor: 'primary.main', color: 'primary.contrastText' }}>{initials}</Avatar>}
              {!b.logoUrl && <Typography variant="body2" fontWeight={700} noWrap>{b.appName}</Typography>}
            </Stack>
            <Divider />
            <List dense>
              {[['Home', <HomeOutlinedIcon key="h" fontSize="small" />], ['Leave', <EventAvailableOutlinedIcon key="l" fontSize="small" />], ['Payslips', <ReceiptLongOutlinedIcon key="p" fontSize="small" />]].map(([t, icon], i) => (
                <ListItemButton key={t as string} sx={{ mx: 0.5, borderRadius: 2, ...(i === 1 ? { bgcolor: brandTint(theme), color: 'primary.main', '& .MuiListItemIcon-root': { color: 'primary.main' } } : {}) }}>
                  <ListItemIcon sx={{ minWidth: 30 }}>{icon}</ListItemIcon><ListItemText primary={t as string} primaryTypographyProps={{ fontWeight: i === 1 ? 600 : 400, fontSize: 13 }} />
                </ListItemButton>
              ))}
            </List>
          </Box>
          <Stack spacing={1.5} sx={{ flex: 1, p: 2, minWidth: 0 }}>
            <Typography variant="h3">Leave</Typography>
            <Stack direction="row" spacing={1.5}>
              {[['Annual leave', '14.5'], ['Sick leave', '7']].map(([t, v]) => (
                <Card key={t} sx={{ flex: 1 }}><CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Typography variant="caption" color="text.secondary">{t}</Typography>
                  <Typography variant="h2" color="primary.main">{v}</Typography>
                  <LinearProgress variant="determinate" value={t === 'Annual leave' ? 60 : 30} color={t === 'Annual leave' ? 'primary' : 'secondary'} sx={{ mt: 1, borderRadius: 1 }} />
                </CardContent></Card>
              ))}
            </Stack>
            <TextField size="small" label="Reason" defaultValue="Family visit" />
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              <Button variant="contained" size="small">Apply for leave</Button>
              <Button variant="outlined" size="small">Save draft</Button>
              <Button color="secondary" size="small">Cancel</Button>
            </Stack>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Chip size="small" color="primary" label="Approved" /><Chip size="small" color="secondary" variant="outlined" label="Pending" />
              <Switch size="small" defaultChecked /><Typography variant="body2">Email me</Typography>
            </Stack>
            <Alert severity="info" sx={{ py: 0 }}>Your manager will be notified.</Alert>
          </Stack>
        </Box>
      </Paper>
    </ThemeProvider>
  )
}

const toSettings = (v: BrandingView): BrandingSettings => ({
  appName: v.appName, welcomeText: v.welcomeText, primary: v.primary, secondary: v.secondary, defaultMode: v.defaultMode, cornerRadius: v.cornerRadius,
})

export function BrandingPage() {
  const outer = useTheme()
  const qc = useQueryClient()
  const data = useQuery({ queryKey: ['branding'], queryFn: brandingApi.get })
  const [form, setForm] = useState<BrandingSettings | null>(null)
  const [previewDark, setPreviewDark] = useState(outer.palette.mode === 'dark')
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const file = useRef<HTMLInputElement>(null)

  useEffect(() => { if (data.data && !form) setForm(toSettings(data.data)) }, [data.data, form])

  const done = (v: BrandingView, message: string) => { qc.setQueryData(['branding'], v); setToast(message); setError(null) }
  const save = useMutation({ mutationFn: (b: BrandingSettings) => brandingApi.save(b), onSuccess: (v) => { done(v, 'Look and feel saved for everyone.'); setForm(toSettings(v)) }, onError: (e) => setError(errorMessage(e)) })
  const upload = useMutation({ mutationFn: (f: File) => brandingApi.uploadLogo(f), onSuccess: (v) => done(v, 'Logo updated.'), onError: (e) => setError(errorMessage(e)) })
  const removeLogo = useMutation({ mutationFn: brandingApi.removeLogo, onSuccess: (v) => done(v, 'Logo removed.'), onError: (e) => setError(errorMessage(e)) })

  if (data.isLoading || (!form && !data.isError)) return <LoadingState rows={6} />
  if (data.isError || !form) return <ErrorState error={data.error} />

  const set = (p: Partial<BrandingSettings>) => setForm({ ...form, ...p })
  const candidate: BrandingView = { ...form, logoUrl: data.data!.logoUrl }
  const onWhite = contrast(form.primary, '#ffffff')
  const changed = JSON.stringify(form) !== JSON.stringify(toSettings(data.data!))
  const valid = isHex(form.primary) && isHex(form.secondary) && form.appName.trim().length > 0 && onWhite >= 3

  return (
    <Stack spacing={3}>
      <Box>
        <Typography variant="h1">Look and feel</Typography>
        <Typography variant="body2" color="text.secondary">
          The portal name, logo and colours everyone sees, including on the sign-in page and the installed app. Each person can still switch between light and dark.
        </Typography>
      </Box>
      {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gap: 3, gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 5fr) minmax(0, 6fr)' }, alignItems: 'start' }}>
        <Stack spacing={3}>
          <Card><CardContent>
            <Stack spacing={2}>
              <Typography variant="subtitle1">Name and logo</Typography>
              <TextField label="Portal name" value={form.appName} onChange={(e) => set({ appName: e.target.value })} inputProps={{ maxLength: 60 }}
                helperText="Shown in the menu, on the sign-in page and as the browser tab title" />
              <TextField label="Sign-in welcome text (optional)" value={form.welcomeText ?? ''} onChange={(e) => set({ welcomeText: e.target.value || null })}
                inputProps={{ maxLength: 200 }} multiline minRows={2} placeholder="Leave, payslips, claims and HR requests in one place." />
              <Stack direction="row" spacing={2} alignItems="center">
                <Box sx={{ width: 140, height: 64, border: 1, borderColor: 'divider', borderRadius: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', bgcolor: 'background.default', flexShrink: 0 }}>
                  {data.data!.logoUrl ? <Box component="img" src={data.data!.logoUrl} alt="Current logo" sx={{ maxHeight: 52, maxWidth: 124, objectFit: 'contain' }} />
                    : <Typography variant="caption" color="text.secondary">No logo</Typography>}
                </Box>
                <Stack spacing={1}>
                  <Stack direction="row" spacing={1}>
                    <Button variant="outlined" size="small" onClick={() => file.current?.click()} disabled={upload.isPending}>{upload.isPending ? 'Uploading…' : data.data!.logoUrl ? 'Replace logo' : 'Upload logo'}</Button>
                    {data.data!.logoUrl && <Button size="small" color="error" onClick={() => removeLogo.mutate()} disabled={removeLogo.isPending}>Remove</Button>}
                  </Stack>
                  <Typography variant="caption" color="text.secondary">PNG, JPEG or WebP up to 512 KB. A wide logo on a transparent background works best. It is saved straight away.</Typography>
                </Stack>
                <input ref={file} type="file" hidden accept="image/png,image/jpeg,image/webp"
                  onChange={(e) => { const f = e.target.files?.[0]; if (f) upload.mutate(f); e.target.value = '' }} />
              </Stack>
            </Stack>
          </CardContent></Card>

          <Card><CardContent>
            <Stack spacing={2}>
              <Typography variant="subtitle1">Colours</Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(92px, 1fr))', gap: 1 }}>
                {presets.map((p) => {
                  const selected = p.primary === form.primary && p.secondary === form.secondary
                  return (
                    <Tooltip key={p.name} title={`${p.primary} / ${p.secondary}`}>
                      <ButtonBase onClick={() => set({ primary: p.primary, secondary: p.secondary })} aria-pressed={selected}
                        sx={{ flexDirection: 'column', alignItems: 'stretch', border: 2, borderColor: selected ? 'primary.main' : 'divider', borderRadius: 1.5, overflow: 'hidden' }}>
                        <Box sx={{ display: 'flex', height: 34 }}>
                          <Box sx={{ flex: 3, bgcolor: p.primary, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>{selected && <CheckIcon sx={{ color: '#fff' }} fontSize="small" />}</Box>
                          <Box sx={{ flex: 1, bgcolor: p.secondary }} />
                        </Box>
                        <Typography variant="caption" sx={{ py: 0.5 }}>{p.name}</Typography>
                      </ButtonBase>
                    </Tooltip>
                  )
                })}
              </Box>
              <ColourField label="Main colour" value={form.primary} onChange={(v) => set({ primary: v })}
                helper={<Stack component="span" direction="row" spacing={1} alignItems="center"><span>Buttons, links, selected menu item. White text on it:</span><ContrastChip ratio={onWhite} /></Stack>} />
              {onWhite < 4.5 && <Alert severity={onWhite < 3 ? 'error' : 'warning'}>
                {onWhite < 3 ? 'White text on this colour is too hard to read, so it cannot be saved. Choose a darker shade.' : 'White text on this colour is readable only at larger sizes. A slightly darker shade is recommended.'}
              </Alert>}
              <ColourField label="Accent colour" value={form.secondary} onChange={(v) => set({ secondary: v })}
                helper={<span>Secondary highlights and charts. In dark mode both colours are lightened automatically (main becomes {forDark(form.primary)}).</span>} />
            </Stack>
          </CardContent></Card>

          <Card><CardContent>
            <Stack spacing={2}>
              <Typography variant="subtitle1">Shape and mode</Typography>
              <Box>
                <Typography variant="body2" sx={{ mb: 1 }}>Corners</Typography>
                <ToggleButtonGroup exclusive size="small" value={form.cornerRadius} onChange={(_, v: number | null) => v != null && set({ cornerRadius: v })}>
                  <ToggleButton value={2}>Square</ToggleButton><ToggleButton value={8}>Soft</ToggleButton><ToggleButton value={12}>Rounded</ToggleButton><ToggleButton value={18}>Round</ToggleButton>
                </ToggleButtonGroup>
              </Box>
              <Box>
                <Typography variant="body2" sx={{ mb: 1 }}>Default for people who have not chosen light or dark themselves</Typography>
                <ToggleButtonGroup exclusive size="small" value={form.defaultMode} onChange={(_, v: DefaultMode | null) => v && set({ defaultMode: v })}>
                  <ToggleButton value="system">Follow the device</ToggleButton><ToggleButton value="light">Light</ToggleButton><ToggleButton value="dark">Dark</ToggleButton>
                </ToggleButtonGroup>
              </Box>
            </Stack>
          </CardContent></Card>

          <Paper variant="outlined" sx={{ position: 'sticky', bottom: 16, zIndex: 2, p: 1.5, display: 'flex', gap: 1, justifyContent: 'flex-end', flexWrap: 'wrap', boxShadow: 3 }}>
            <Button onClick={() => set({ ...toSettings(defaultBranding), appName: form.appName, welcomeText: form.welcomeText })}>Default colours</Button>
            <Button disabled={!changed} onClick={() => setForm(toSettings(data.data!))}>Undo changes</Button>
            <Button variant="contained" disabled={!changed || !valid || save.isPending} onClick={() => save.mutate(form)}>{save.isPending ? 'Saving…' : 'Save for everyone'}</Button>
          </Paper>
        </Stack>

        <Box sx={{ position: { lg: 'sticky' }, top: { lg: 88 } }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
            <Typography variant="subtitle1">Preview{changed ? ' (not saved yet)' : ''}</Typography>
            <ToggleButtonGroup exclusive size="small" value={previewDark ? 'dark' : 'light'} onChange={(_, v) => v && setPreviewDark(v === 'dark')}>
              <ToggleButton value="light">Light</ToggleButton><ToggleButton value="dark">Dark</ToggleButton>
            </ToggleButtonGroup>
          </Stack>
          <Preview b={candidate} dark={previewDark} />
          <Typography variant="subtitle1" sx={{ mt: 3, mb: 1 }}>Sign-in page</Typography>
          <ThemeProvider theme={buildTheme(candidate, false)}>
            <Box sx={{ borderRadius: 2, p: 3, color: '#fff', background: brandGradient(buildTheme(candidate, false)) }}>
              <Typography fontWeight={700} fontSize="1.3rem">{form.appName || ' '}</Typography>
              <Typography sx={{ opacity: 0.9 }}>{form.welcomeText || 'Leave, payslips, claims and HR requests in one place.'}</Typography>
            </Box>
          </ThemeProvider>
        </Box>
      </Box>
      <Snackbar open={!!toast} autoHideDuration={5000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
