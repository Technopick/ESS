import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Snackbar, Stack, Tab, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { requestsApi, type HrRequest, type HrRequestType } from '../api/modules'
import { errorMessage } from '../api/client'
import { Permissions, useAuth } from '../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { StatusChip } from '../components/Status'
import { ApprovalDialog } from '../components/ApprovalDialog'

function NewRequestDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const types = useQuery({ queryKey: ['request-types'], queryFn: requestsApi.types, enabled: open })
  const [type, setType] = useState<HrRequestType | null>(null)
  const [subject, setSubject] = useState('')
  const [fields, setFields] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const reset = () => { setType(null); setSubject(''); setFields({}); setError(null) }
  const submit = useMutation({
    mutationFn: () => requestsApi.submit({ typeId: type!.id, subject, fields }),
    onSuccess: (r) => { onDone(`${r.reference} submitted.`); reset(); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const missing = !type || !subject.trim() || type.fields.some((f) => f.required && !fields[f.name]?.trim())
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>New HR request</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <TextField select label="What do you need?" value={type?.id ?? ''} onChange={(e) => { setType(types.data?.find((t) => t.id === e.target.value) ?? null); setFields({}) }}>
            {(types.data ?? []).map((t) => <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>)}
          </TextField>
          {type?.description && <Typography variant="body2" color="text.secondary">{type.description}</Typography>}
          {type?.isSensitive && <Alert severity="info">Only HR officers handling this request can see it.</Alert>}
          {type && (
            <>
              <TextField label="Subject" value={subject} onChange={(e) => setSubject(e.target.value)} inputProps={{ maxLength: 200 }} />
              {type.fields.map((f) => (
                <TextField key={f.name} label={f.label} required={f.required} select={f.type === 'select'} multiline={f.type === 'textarea'} minRows={f.type === 'textarea' ? 3 : undefined}
                  type={f.type === 'number' ? 'number' : f.type === 'date' ? 'date' : 'text'} InputLabelProps={f.type === 'date' ? { shrink: true } : undefined}
                  inputProps={{ maxLength: f.maxLength ?? undefined }} value={fields[f.name] ?? ''} onChange={(e) => setFields({ ...fields, [f.name]: e.target.value })}>
                  {(f.options ?? []).map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}
                </TextField>
              ))}
              {type.slaHours && <Typography variant="caption" color="text.secondary">HR aims to respond within {type.slaHours >= 24 ? `${Math.round(type.slaHours / 24)} working day(s)` : `${type.slaHours} hours`}.</Typography>}
            </>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={missing || submit.isPending} onClick={() => submit.mutate()}>Submit</Button>
      </DialogActions>
    </Dialog>
  )
}

function RequestTable({ rows, showEmployee, actions }: { rows: HrRequest[]; showEmployee?: boolean; actions: (r: HrRequest) => React.ReactNode }) {
  return (
    <TableContainer>
      <Table size="small">
        <TableHead><TableRow><TableCell>Reference</TableCell>{showEmployee && <TableCell>Employee</TableCell>}<TableCell>Request</TableCell><TableCell>Due</TableCell><TableCell>Status</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>
          {rows.map((r) => (
            <TableRow key={r.id} hover>
              <TableCell>{r.reference}<Typography variant="caption" display="block" color="text.secondary">{r.submittedAtUtc ? dayjs(r.submittedAtUtc).format('D MMM YYYY') : ''}</Typography></TableCell>
              {showEmployee && <TableCell>{r.employeeName}</TableCell>}
              <TableCell>{r.type}<Typography variant="caption" display="block" color="text.secondary">{r.subject}</Typography>
                {r.resolution && <Typography variant="body2" sx={{ mt: 0.5 }}>{r.resolution}</Typography>}</TableCell>
              <TableCell>{r.dueAtUtc && !r.resolvedAtUtc ? <Typography variant="body2" color={dayjs(r.dueAtUtc).isBefore(dayjs()) ? 'error' : undefined}>{dayjs(r.dueAtUtc).format('D MMM HH:mm')}</Typography> : '–'}</TableCell>
              <TableCell><StatusChip status={r.status} /></TableCell>
              <TableCell align="right">{actions(r)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}

function HrQueue({ onToast }: { onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const [status, setStatus] = useState('Approved')
  const q = useQuery({ queryKey: ['requests-queue', status], queryFn: () => requestsApi.queue(status) })
  const [resolving, setResolving] = useState<HrRequest | null>(null)
  const [text, setText] = useState('')
  const resolve = useMutation({
    mutationFn: () => requestsApi.resolve(resolving!.id, text),
    onSuccess: () => { onToast(`${resolving!.reference} resolved.`); setResolving(null); setText(''); qc.invalidateQueries({ queryKey: ['requests-queue'] }) },
    onError: (e) => onToast(errorMessage(e)),
  })
  return (
    <Stack spacing={2}>
      <TextField select size="small" label="Show" value={status} onChange={(e) => setStatus(e.target.value)} sx={{ width: 220 }}>
        <MenuItem value="Approved">Ready to action</MenuItem><MenuItem value="InApproval">Waiting for approval</MenuItem><MenuItem value="Completed">Resolved</MenuItem><MenuItem value="">All</MenuItem>
      </TextField>
      {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} /> : q.data!.items.length === 0 ? <EmptyState title="Nothing in the queue" /> : (
        <RequestTable rows={q.data!.items} showEmployee actions={(r) => r.status === 'Approved' ? <Button size="small" onClick={() => setResolving(r)}>Resolve</Button> : null} />
      )}
      <Dialog open={!!resolving} onClose={() => setResolving(null)} fullWidth maxWidth="sm">
        <DialogTitle>Resolve {resolving?.reference}</DialogTitle>
        <DialogContent>
          <Stack spacing={1} sx={{ mt: 1 }}>
            {resolving && Object.entries(resolving.fields).map(([k, v]) => <Typography key={k} variant="body2"><strong>{k}:</strong> {v}</Typography>)}
            <TextField multiline minRows={3} label="Response to the employee" value={text} onChange={(e) => setText(e.target.value)} />
          </Stack>
        </DialogContent>
        <DialogActions><Button onClick={() => setResolving(null)}>Cancel</Button><Button variant="contained" disabled={!text.trim() || resolve.isPending} onClick={() => resolve.mutate()}>Resolve</Button></DialogActions>
      </Dialog>
    </Stack>
  )
}

export function RequestsPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const [tab, setTab] = useState(0)
  const [creating, setCreating] = useState(false)
  const [timeline, setTimeline] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const mine = useQuery({ queryKey: ['requests-mine'], queryFn: requestsApi.mine })
  const refresh = (m: string) => { setToast(m); qc.invalidateQueries({ queryKey: ['requests-mine'] }) }
  const cancel = useMutation({ mutationFn: requestsApi.cancel, onSuccess: () => refresh('Request cancelled.'), onError: (e) => setToast(errorMessage(e)) })
  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">HR requests</Typography>
        <Button variant="contained" onClick={() => setCreating(true)}>New request</Button>
      </Stack>
      {can(Permissions.RequestsManage) && <Tabs value={tab} onChange={(_, v) => setTab(v)}><Tab label="My requests" /><Tab label="HR queue" /></Tabs>}
      <Card><CardContent>
        {tab === 1 ? <HrQueue onToast={setToast} /> : mine.isLoading ? <LoadingState /> : mine.isError ? <ErrorState error={mine.error} onRetry={() => mine.refetch()} />
          : mine.data!.length === 0 ? <EmptyState title="No requests yet" hint="Ask HR for letters, certificates, record updates and more." /> : (
            <RequestTable rows={mine.data!} actions={(r) => (
              <Stack direction="row" spacing={1} justifyContent="flex-end">
                {r.workflowInstanceId && <Button size="small" onClick={() => setTimeline(r.workflowInstanceId)}>History</Button>}
                {['Submitted', 'InApproval', 'ReturnedForCorrection'].includes(r.status) && <Button size="small" color="error" onClick={() => cancel.mutate(r.id)}>Cancel</Button>}
              </Stack>
            )} />
          )}
      </CardContent></Card>
      <NewRequestDialog open={creating} onClose={() => setCreating(false)} onDone={refresh} />
      <ApprovalDialog instanceId={timeline} onClose={() => setTimeline(null)} />
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
