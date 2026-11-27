import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Pagination, Snackbar, Stack,
  Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Tooltip, Typography,
} from '@mui/material'
import { notificationAdminApi, type Channel, type TemplateView } from '../../api/admin'
import { errorMessage } from '../../api/client'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { StatTile } from '../../components/Charts'

const channels: { value: Channel; label: string }[] = [{ value: 'InApp', label: 'In the portal' }, { value: 'Email', label: 'Email' }, { value: 'Sms', label: 'SMS' }]
const channelLabel = (c: string) => channels.find((x) => x.value === c)?.label ?? c
const statusColor = { Pending: 'info', Sent: 'success', Delivered: 'success', Failed: 'error', Suppressed: 'default' } as const
const SMS_LIMIT = 459

interface Editing { event: string; description: string; channel: Channel; template: TemplateView | null }

function TemplateDialog({ editing, placeholders, onClose, onDone }: { editing: Editing; placeholders: string[]; onClose: () => void; onDone: (m: string) => void }) {
  const sms = editing.channel === 'Sms'
  const [f, setF] = useState({ subject: editing.template?.subject ?? '', body: editing.template?.body ?? '', isActive: editing.template?.isActive ?? true })
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<{ subject: string | null; body: string } | null>(null)
  const payload = () => ({ subject: sms ? null : f.subject || null, body: f.body, isActive: f.isActive })
  const save = useMutation({ mutationFn: () => notificationAdminApi.save(editing.event, editing.channel, payload()), onSuccess: () => { onDone('Template saved.'); onClose() }, onError: (e) => setError(errorMessage(e)) })
  const reset = useMutation({ mutationFn: () => notificationAdminApi.reset(editing.event, editing.channel), onSuccess: () => { onDone(sms ? 'SMS template removed. No SMS is sent for this event.' : 'Template removed. The built-in wording is used again.'); onClose() }, onError: (e) => setError(errorMessage(e)) })
  const show = useMutation({ mutationFn: () => notificationAdminApi.preview(payload()), onSuccess: setPreview, onError: (e) => setError(errorMessage(e)) })
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{editing.description}: {channelLabel(editing.channel)}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <Typography variant="body2" color="text.secondary">
            Placeholders: {placeholders.map((p) => <code key={p} style={{ marginRight: 8 }}>{`{${p}}`}</code>)}
          </Typography>
          {sms && <Alert severity="info">An SMS is only sent for events that have an active SMS template. Keep it short and never put pay amounts or other personal details in an SMS; link to the portal instead.</Alert>}
          {!sms && <TextField label="Subject" value={f.subject} onChange={(e) => setF({ ...f, subject: e.target.value })} helperText="Empty uses {subject}" />}
          <TextField label="Message" value={f.body} onChange={(e) => setF({ ...f, body: e.target.value })} multiline minRows={sms ? 3 : 6}
            helperText={sms ? `${f.body.length} of ${SMS_LIMIT} characters (${Math.max(1, Math.ceil(f.body.length / 153))} SMS before placeholders are filled)` : 'Plain text. Line breaks are kept.'} error={sms && f.body.length > SMS_LIMIT} />
          <FormControlLabel control={<Checkbox checked={f.isActive} onChange={(e) => setF({ ...f, isActive: e.target.checked })} />} label="Use this template" />
          {preview && <Card variant="outlined"><CardContent>
            <Typography variant="overline">Preview with sample values</Typography>
            {preview.subject && <Typography variant="subtitle1">{preview.subject}</Typography>}
            <Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>{preview.body}</Typography>
          </CardContent></Card>}
        </Stack>
      </DialogContent>
      <DialogActions>
        {editing.template && <Button color="error" sx={{ mr: 'auto' }} onClick={() => reset.mutate()} disabled={reset.isPending}>{sms ? 'Remove SMS template' : 'Use built-in wording'}</Button>}
        <Button onClick={() => show.mutate()} disabled={!f.body.trim() || show.isPending}>Preview</Button>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => save.mutate()} disabled={!f.body.trim() || (sms && f.body.length > SMS_LIMIT) || save.isPending}>Save</Button>
      </DialogActions>
    </Dialog>
  )
}

function Templates({ onToast }: { onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState<Editing | null>(null)
  const data = useQuery({ queryKey: ['notification-templates'], queryFn: notificationAdminApi.templates })
  if (data.isLoading) return <LoadingState rows={6} />
  if (data.isError) return <ErrorState error={data.error} />
  const t = data.data!
  return (
    <Stack spacing={2}>
      {!t.emailConfigured && <Alert severity="warning">Email is not configured on the server, so email notifications are recorded but not sent.</Alert>}
      {!t.smsConfigured && <Alert severity="info">No SMS gateway is configured. SMS templates can be prepared, and messages are recorded as not sent until the gateway is set up (Sms settings on the server).</Alert>}
      <TableContainer><Table size="small">
        <TableHead><TableRow><TableCell>Event</TableCell>{channels.map((c) => <TableCell key={c.value}>{c.label}</TableCell>)}</TableRow></TableHead>
        <TableBody>{t.events.map((e) => (
          <TableRow key={e.code} hover>
            <TableCell>{e.description}<Typography variant="caption" display="block" color="text.secondary">{e.code}</Typography></TableCell>
            {channels.map((c) => {
              const tpl = e.templates.find((x) => x.channel === c.value) ?? null
              const label = tpl ? (tpl.isActive ? 'Custom' : 'Custom (off)') : c.value === 'Sms' ? 'Not sent' : 'Built-in'
              return <TableCell key={c.value}><Button size="small" variant={tpl?.isActive ? 'outlined' : 'text'} onClick={() => setEditing({ event: e.code, description: e.description, channel: c.value, template: tpl })}>{label}</Button></TableCell>
            })}
          </TableRow>
        ))}</TableBody>
      </Table></TableContainer>
      <Typography variant="body2" color="text.secondary">WhatsApp is not available yet; it needs an approved WhatsApp Business provider.</Typography>
      {editing && <TemplateDialog editing={editing} placeholders={t.placeholders} onClose={() => setEditing(null)} onDone={(m) => { onToast(m); qc.invalidateQueries({ queryKey: ['notification-templates'] }) }} />}
    </Stack>
  )
}

function Deliveries({ onToast }: { onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const [q, setQ] = useState<{ status?: string; channel?: string; eventCode?: string; recipient?: string; page: number }>({ page: 1 })
  const [recipient, setRecipient] = useState('')
  const stats = useQuery({ queryKey: ['delivery-stats'], queryFn: notificationAdminApi.stats, refetchInterval: 60_000 })
  const list = useQuery({ queryKey: ['deliveries', q], queryFn: () => notificationAdminApi.deliveries(q) })
  const retry = useMutation({
    mutationFn: notificationAdminApi.retry,
    onSuccess: (r) => { onToast(r.queued === 0 ? 'Nothing to retry.' : `${r.queued} message${r.queued === 1 ? '' : 's'} queued again.`); qc.invalidateQueries({ queryKey: ['deliveries'] }); qc.invalidateQueries({ queryKey: ['delivery-stats'] }) },
    onError: (e) => onToast(errorMessage(e)),
  })
  const set = (p: object) => setQ({ ...q, page: 1, ...p })
  return (
    <Stack spacing={2}>
      {stats.data && <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))' }}>
        <StatTile label="Sent today" value={stats.data.sentToday} />
        <StatTile label="Waiting to send" value={stats.data.pending} />
        <StatTile label="Failed" value={stats.data.failed} />
        <StatTile label="Not sent today" value={stats.data.suppressedToday} hint="No gateway, no phone number or email, or switched off" />
      </Box>}
      <Stack direction="row" spacing={2} useFlexGap flexWrap="wrap" alignItems="center" component="form" onSubmit={(e) => { e.preventDefault(); set({ recipient }) }}>
        <TextField select size="small" label="Status" value={q.status ?? ''} onChange={(e) => set({ status: e.target.value })} sx={{ minWidth: 150 }}>
          <MenuItem value="">Any status</MenuItem>{Object.keys(statusColor).map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="Channel" value={q.channel ?? ''} onChange={(e) => set({ channel: e.target.value })} sx={{ minWidth: 150 }}>
          <MenuItem value="">Any channel</MenuItem>{channels.map((c) => <MenuItem key={c.value} value={c.value}>{c.label}</MenuItem>)}
        </TextField>
        <TextField size="small" label="Recipient" value={recipient} onChange={(e) => setRecipient(e.target.value)} />
        <Button type="submit" variant="outlined">Search</Button>
        <Box sx={{ flex: 1 }} />
        <Button variant="contained" disabled={!stats.data?.failed || retry.isPending} onClick={() => retry.mutate(undefined)}>Retry all failed</Button>
      </Stack>
      {list.isLoading ? <LoadingState rows={6} /> : list.isError ? <ErrorState error={list.error} /> : list.data!.items.length === 0 ? <EmptyState title="No messages match" /> : (
        <>
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>When</TableCell><TableCell>Event</TableCell><TableCell>Channel</TableCell><TableCell>Recipient</TableCell><TableCell>Subject</TableCell><TableCell>Status</TableCell><TableCell align="right">Tries</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>{list.data!.items.map((d) => (
              <TableRow key={d.id} hover>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{dayjs(d.createdAtUtc).format('D MMM, HH:mm')}</TableCell>
                <TableCell>{d.eventCode}</TableCell>
                <TableCell>{channelLabel(d.channel)}</TableCell>
                <TableCell>{d.recipient}</TableCell>
                <TableCell>{d.subject ?? '–'}</TableCell>
                <TableCell>
                  {d.lastError ? <Tooltip title={d.lastError}><Chip size="small" color={statusColor[d.status]} label={d.status} /></Tooltip> : <Chip size="small" color={statusColor[d.status]} label={d.status} />}
                  {d.readAtUtc && <Typography variant="caption" display="block" color="text.secondary">Read {dayjs(d.readAtUtc).format('D MMM, HH:mm')}</Typography>}
                </TableCell>
                <TableCell align="right">{d.attempts}</TableCell>
                <TableCell>{d.status === 'Failed' && <Button size="small" onClick={() => retry.mutate(d.id)}>Retry</Button>}</TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></TableContainer>
          <Pagination count={Math.max(1, Math.ceil(list.data!.totalCount / list.data!.pageSize))} page={q.page} onChange={(_, p) => setQ({ ...q, page: p })} />
        </>
      )}
    </Stack>
  )
}

export function NotificationsAdminPage() {
  const [tab, setTab] = useState(0)
  const [toast, setToast] = useState<string | null>(null)
  return (
    <Stack spacing={3}>
      <Typography variant="h1">Notifications</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}><Tab label="Templates" /><Tab label="Delivery history" /></Tabs>
      <Card><CardContent>{tab === 0 ? <Templates onToast={setToast} /> : <Deliveries onToast={setToast} />}</CardContent></Card>
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
