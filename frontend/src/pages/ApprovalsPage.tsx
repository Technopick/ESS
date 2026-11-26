import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Autocomplete, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Pagination, Stack, Switch, FormControlLabel,
  Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { approvalsApi, documentTypeLabel, money } from '../api/modules'
import { errorMessage } from '../api/client'
import { Permissions, useAuth } from '../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { ApprovalDialog } from '../components/ApprovalDialog'
import { StatusChip } from '../components/Status'

function Inbox({ onOpen }: { onOpen: (id: string) => void }) {
  const q = useQuery({ queryKey: ['inbox'], queryFn: approvalsApi.inbox, refetchInterval: 60_000 })
  if (q.isLoading) return <LoadingState />
  if (q.isError) return <ErrorState error={q.error} onRetry={() => q.refetch()} />
  if (q.data!.length === 0) return <EmptyState title="Nothing is waiting for you" hint="Requests that need your approval appear here." />
  return (
    <TableContainer>
      <Table size="small">
        <TableHead><TableRow><TableCell>Request</TableCell><TableCell>From</TableCell><TableCell>Step</TableCell><TableCell>Due</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>
          {q.data!.map((t) => (
            <TableRow key={t.taskId} hover sx={{ cursor: 'pointer' }} onClick={() => onOpen(t.instanceId)}>
              <TableCell>
                <Typography fontWeight={600}>{documentTypeLabel(t.documentType)} {t.reference}</Typography>
                <Typography variant="body2" color="text.secondary">{t.summary}{t.amount ? ` · ${money(t.amount)}` : ''}</Typography>
              </TableCell>
              <TableCell>{t.requester}</TableCell>
              <TableCell>
                {t.step}
                {t.onBehalfOf && <Chip size="small" sx={{ ml: 1 }} label={`for ${t.onBehalfOf}`} />}
                {t.escalated && <Chip size="small" color="warning" sx={{ ml: 1 }} label="Escalated" />}
              </TableCell>
              <TableCell>{t.dueAtUtc ? <Typography variant="body2" color={t.overdue ? 'error' : undefined}>{dayjs(t.dueAtUtc).format('D MMM HH:mm')}</Typography> : '–'}</TableCell>
              <TableCell align="right"><Button size="small">Review</Button></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}

function Decided({ onOpen }: { onOpen: (id: string) => void }) {
  const [page, setPage] = useState(1)
  const q = useQuery({ queryKey: ['decided', page], queryFn: () => approvalsApi.decided(page) })
  if (q.isLoading) return <LoadingState />
  if (q.isError) return <ErrorState error={q.error} onRetry={() => q.refetch()} />
  if (q.data!.items.length === 0) return <EmptyState title="You have not decided any requests yet" />
  return (
    <>
      <Table size="small">
        <TableHead><TableRow><TableCell>Request</TableCell><TableCell>From</TableCell><TableCell>Your decision</TableCell><TableCell>When</TableCell></TableRow></TableHead>
        <TableBody>
          {q.data!.items.map((t) => (
            <TableRow key={t.taskId} hover sx={{ cursor: 'pointer' }} onClick={() => onOpen(t.instanceId)}>
              <TableCell>{documentTypeLabel(t.documentType)} {t.reference}<Typography variant="body2" color="text.secondary">{t.summary}</Typography></TableCell>
              <TableCell>{t.requester}</TableCell>
              <TableCell><StatusChip status={t.decision === 'Returned' ? 'ReturnedForCorrection' : t.decision} /></TableCell>
              <TableCell>{t.actedAtUtc ? dayjs(t.actedAtUtc).format('D MMM YYYY HH:mm') : ''}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <Pagination sx={{ mt: 2 }} count={Math.max(1, Math.ceil(q.data!.totalCount / q.data!.pageSize))} page={page} onChange={(_, p) => setPage(p)} />
    </>
  )
}

function Delegations() {
  const qc = useQueryClient()
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [form, setForm] = useState({ toUserId: '', starts: dayjs().format('YYYY-MM-DD'), ends: dayjs().add(7, 'day').format('YYYY-MM-DD'), documentType: '', reason: '' })
  const [error, setError] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['delegations'], queryFn: approvalsApi.delegations })
  const people = useQuery({ queryKey: ['approvers', search], queryFn: () => approvalsApi.approvers(search), enabled: open })
  const save = useMutation({
    mutationFn: () => approvalsApi.delegate({
      toUserId: form.toUserId, startsAtUtc: dayjs(form.starts).startOf('day').toISOString(), endsAtUtc: dayjs(form.ends).endOf('day').toISOString(),
      documentType: form.documentType || null, reason: form.reason || null,
    }),
    onSuccess: () => { setOpen(false); qc.invalidateQueries({ queryKey: ['delegations'] }) },
    onError: (e) => setError(errorMessage(e)),
  })
  const end = useMutation({ mutationFn: approvalsApi.endDelegation, onSuccess: () => qc.invalidateQueries({ queryKey: ['delegations'] }) })
  return (
    <Stack spacing={2}>
      <Typography variant="body2" color="text.secondary">
        While you are away, new approvals go to the person you choose. Approvals already assigned to you stay with you.
      </Typography>
      <div><Button variant="outlined" onClick={() => { setError(null); setOpen(true) }}>Delegate my approvals</Button></div>
      {list.isLoading ? <LoadingState /> : (list.data ?? []).length === 0 ? <EmptyState title="No delegations" /> : (
        <Table size="small">
          <TableHead><TableRow><TableCell>To</TableCell><TableCell>From</TableCell><TableCell>Until</TableCell><TableCell>Requests</TableCell><TableCell /></TableRow></TableHead>
          <TableBody>
            {list.data!.map((d) => (
              <TableRow key={d.id}>
                <TableCell>{d.toUser} {d.active && <Chip size="small" color="success" label="Active" />}</TableCell>
                <TableCell>{dayjs(d.startsAtUtc).format('D MMM YYYY')}</TableCell>
                <TableCell>{dayjs(d.endsAtUtc).format('D MMM YYYY')}</TableCell>
                <TableCell>{d.documentType ? documentTypeLabel(d.documentType) : 'All'}</TableCell>
                <TableCell align="right">{dayjs(d.endsAtUtc).isAfter(dayjs()) && <Button size="small" color="error" onClick={() => end.mutate(d.id)}>End</Button>}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle>Delegate approvals</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {error && <Alert severity="error">{error}</Alert>}
            <Autocomplete options={people.data ?? []} getOptionLabel={(o) => o.name} onInputChange={(_, v) => setSearch(v)}
              onChange={(_, v) => setForm({ ...form, toUserId: v?.userId ?? '' })} renderInput={(p) => <TextField {...p} label="Delegate to" />} />
            <TextField type="date" label="From" InputLabelProps={{ shrink: true }} value={form.starts} onChange={(e) => setForm({ ...form, starts: e.target.value })} />
            <TextField type="date" label="Until" InputLabelProps={{ shrink: true }} value={form.ends} onChange={(e) => setForm({ ...form, ends: e.target.value })} />
            <TextField select label="Requests" value={form.documentType} onChange={(e) => setForm({ ...form, documentType: e.target.value })}>
              <MenuItem value="">All requests</MenuItem>
              {['Leave', 'Expense', 'ProfileChange', 'HrRequest', 'Attendance', 'Training'].map((t) => <MenuItem key={t} value={t}>{documentTypeLabel(t)}</MenuItem>)}
            </TextField>
            <TextField label="Reason (optional)" value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>Cancel</Button>
          <Button variant="contained" disabled={!form.toUserId || save.isPending} onClick={() => save.mutate()}>Delegate</Button>
        </DialogActions>
      </Dialog>
    </Stack>
  )
}

function Monitor({ onOpen }: { onOpen: (id: string) => void }) {
  const [overdue, setOverdue] = useState(false)
  const [page, setPage] = useState(1)
  const q = useQuery({ queryKey: ['monitor', overdue, page], queryFn: () => approvalsApi.monitor(overdue, page) })
  return (
    <Stack spacing={1}>
      <FormControlLabel control={<Switch checked={overdue} onChange={(e) => { setOverdue(e.target.checked); setPage(1) }} />} label="Overdue only" />
      {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} /> : q.data!.items.length === 0 ? <EmptyState title="No approvals in progress" /> : (
        <>
          <Table size="small">
            <TableHead><TableRow><TableCell>Request</TableCell><TableCell>From</TableCell><TableCell>Submitted</TableCell><TableCell>With</TableCell><TableCell>Due</TableCell></TableRow></TableHead>
            <TableBody>
              {q.data!.items.map((m) => (
                <TableRow key={m.instanceId} hover sx={{ cursor: 'pointer' }} onClick={() => onOpen(m.instanceId)}>
                  <TableCell>{documentTypeLabel(m.documentType)} {m.reference}<Typography variant="body2" color="text.secondary">{m.summary}</Typography></TableCell>
                  <TableCell>{m.requester}</TableCell>
                  <TableCell>{dayjs(m.submittedAtUtc).format('D MMM')}</TableCell>
                  <TableCell>{m.pendingWith ?? '–'}</TableCell>
                  <TableCell>{m.dueAtUtc ? <Typography variant="body2" color={m.overdue ? 'error' : undefined}>{dayjs(m.dueAtUtc).format('D MMM HH:mm')}</Typography> : '–'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pagination count={Math.max(1, Math.ceil(q.data!.totalCount / q.data!.pageSize))} page={page} onChange={(_, p) => setPage(p)} />
        </>
      )}
    </Stack>
  )
}

export function ApprovalsPage() {
  const { can } = useAuth()
  const [tab, setTab] = useState('inbox')
  const [open, setOpen] = useState<string | null>(null)
  return (
    <Stack spacing={3}>
      <Typography variant="h1">Approvals</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable">
        <Tab value="inbox" label="Waiting for me" />
        <Tab value="decided" label="Decided" />
        {can(Permissions.ApprovalsAct) && <Tab value="delegate" label="Delegation" />}
        {can(Permissions.ApprovalsMonitor) && <Tab value="monitor" label="All in progress" />}
      </Tabs>
      <Card><CardContent>
        {tab === 'inbox' && <Inbox onOpen={setOpen} />}
        {tab === 'decided' && <Decided onOpen={setOpen} />}
        {tab === 'delegate' && <Delegations />}
        {tab === 'monitor' && <Monitor onOpen={setOpen} />}
      </CardContent></Card>
      <ApprovalDialog instanceId={open} onClose={() => setOpen(null)} />
    </Stack>
  )
}
