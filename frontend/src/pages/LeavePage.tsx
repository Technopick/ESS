import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Grid2 as Grid, MenuItem, Pagination,
  Snackbar, Stack, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { leaveApi, type LeaveInput, type LeaveView } from '../api/modules'
import { errorMessage } from '../api/client'
import { Permissions, useAuth } from '../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { Statuses } from '../components/Status'
import { ApprovalDialog } from '../components/ApprovalDialog'

const fmt = (d: string) => dayjs(d).format('ddd D MMM YYYY')

function ApplyDialog({ open, editing, onClose, onDone }: { open: boolean; editing: LeaveView | null; onClose: () => void; onDone: (m: string) => void }) {
  const types = useQuery({ queryKey: ['leave-types'], queryFn: leaveApi.types })
  const blank: LeaveInput = { leaveTypeId: '', startDate: dayjs().add(7, 'day').format('YYYY-MM-DD'), endDate: dayjs().add(7, 'day').format('YYYY-MM-DD'), startHalfDay: false, endHalfDay: false, reason: '' }
  const [form, setForm] = useState<LeaveInput>(blank)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!open) return
    setError(null)
    setForm(editing ? { leaveTypeId: editing.leaveTypeId, startDate: editing.startDate, endDate: editing.endDate, startHalfDay: editing.startHalfDay, endHalfDay: editing.endHalfDay, reason: editing.reason ?? '', relieverEmployeeId: editing.relieverEmployeeId } : blank)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, editing])
  const type = types.data?.find((t) => t.id === form.leaveTypeId)
  const ready = !!form.leaveTypeId && !!form.startDate && !!form.endDate
  const [debounced, setDebounced] = useState(form)
  useEffect(() => { const t = setTimeout(() => setDebounced(form), 400); return () => clearTimeout(t) }, [form])
  const check = useQuery({
    queryKey: ['leave-check', debounced, editing?.id], enabled: open && ready && debounced.endDate >= debounced.startDate,
    queryFn: () => leaveApi.check(debounced, editing?.id), retry: false,
  })
  const submit = useMutation({
    mutationFn: () => editing ? leaveApi.resubmit(editing.id, form) : leaveApi.submit(form),
    onSuccess: (l) => { onDone(l.status === 'Approved' ? `${l.reference} was approved.` : `${l.reference} was sent for approval.`); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const c = check.data
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{editing ? `Correct ${editing.reference}` : 'Apply for leave'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField select label="Leave type" value={form.leaveTypeId} onChange={(e) => setForm({ ...form, leaveTypeId: e.target.value })}>
            {(types.data ?? []).map((t) => <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>)}
          </TextField>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="date" label="First day" InputLabelProps={{ shrink: true }} value={form.startDate}
              onChange={(e) => setForm({ ...form, startDate: e.target.value, endDate: form.endDate < e.target.value ? e.target.value : form.endDate })} />
            <TextField fullWidth type="date" label="Last day" InputLabelProps={{ shrink: true }} value={form.endDate} inputProps={{ min: form.startDate }}
              onChange={(e) => setForm({ ...form, endDate: e.target.value })} />
          </Stack>
          {type?.allowHalfDay && (
            <Stack direction="row">
              <FormControlLabel control={<Checkbox checked={form.startHalfDay} onChange={(e) => setForm({ ...form, startHalfDay: e.target.checked })} />} label="Start after lunch" />
              <FormControlLabel control={<Checkbox checked={form.endHalfDay} onChange={(e) => setForm({ ...form, endHalfDay: e.target.checked })} />} label="End at lunch" />
            </Stack>
          )}
          <TextField label="Reason (optional)" value={form.reason ?? ''} onChange={(e) => setForm({ ...form, reason: e.target.value })} multiline minRows={2} inputProps={{ maxLength: 500 }} />
          {ready && (check.isFetching ? <Typography variant="body2" color="text.secondary">Checking…</Typography> : check.isError ? <Alert severity="error">{errorMessage(check.error)}</Alert> : c && (
            <Stack spacing={1}>
              <Alert severity={c.errors.length ? 'error' : 'success'} icon={false}>
                <strong>{c.days} working day{c.days === 1 ? '' : 's'}</strong>
                {c.available !== null && <> · {c.available} available, {c.availableAfter} left after this</>}
                {c.errors.map((e) => <div key={e}>{e}</div>)}
              </Alert>
              {c.warnings.map((w) => <Alert key={w} severity="warning">{w}</Alert>)}
            </Stack>
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => submit.mutate()} disabled={submit.isPending || !c?.isValid || check.isFetching}>
          {editing ? 'Resubmit' : 'Submit'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

function Calendar() {
  const [from, setFrom] = useState(dayjs().startOf('week').add(1, 'day').format('YYYY-MM-DD'))
  const to = dayjs(from).add(41, 'day').format('YYYY-MM-DD')
  const q = useQuery({ queryKey: ['leave-calendar', from], queryFn: () => leaveApi.calendar(from, to) })
  const days = useMemo(() => Array.from({ length: 42 }, (_, i) => dayjs(from).add(i, 'day')), [from])
  const people = useMemo(() => [...new Map((q.data ?? []).map((e) => [e.employeeId, e.employeeName])).entries()], [q.data])
  return (
    <Card>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
          <Typography variant="h6">Who is away</Typography>
          <Stack direction="row" spacing={1}>
            <Button onClick={() => setFrom(dayjs(from).subtract(42, 'day').format('YYYY-MM-DD'))}>Earlier</Button>
            <Button onClick={() => setFrom(dayjs(from).add(42, 'day').format('YYYY-MM-DD'))}>Later</Button>
          </Stack>
        </Stack>
        {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} /> : people.length === 0 ? <EmptyState title="Nobody is on leave in these six weeks" /> : (
          <TableContainer sx={{ maxWidth: '100%', overflowX: 'auto' }}>
            <Table size="small" sx={{ '& td, & th': { px: 0.25, py: 0.5 } }}>
              <TableHead>
                <TableRow>
                  <TableCell sx={{ minWidth: 140 }}>{dayjs(from).format('MMM YYYY')}</TableCell>
                  {days.map((d) => <TableCell key={d.toString()} align="center" sx={{ fontSize: 11, color: [0, 6].includes(d.day()) ? 'text.disabled' : undefined }}>{d.format('D')}</TableCell>)}
                </TableRow>
              </TableHead>
              <TableBody>
                {people.map(([id, name]) => (
                  <TableRow key={id}>
                    <TableCell>{name}</TableCell>
                    {days.map((d) => {
                      const e = q.data!.find((x) => x.employeeId === id && !d.isBefore(x.startDate, 'day') && !d.isAfter(x.endDate, 'day'))
                      return (
                        <TableCell key={d.toString()} title={e ? `${e.leaveType ?? 'On leave'} (${e.status === 'Approved' ? 'approved' : 'pending'})` : undefined}
                          sx={{ bgcolor: e ? (e.status === 'Approved' ? 'primary.main' : 'primary.light') : undefined, opacity: e && e.status !== 'Approved' ? 0.5 : 1 }} />
                      )
                    })}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </CardContent>
    </Card>
  )
}

export function LeavePage() {
  const qc = useQueryClient()
  const { can } = useAuth()
  const [tab, setTab] = useState(0)
  const [page, setPage] = useState(1)
  const [applying, setApplying] = useState(false)
  const [editing, setEditing] = useState<LeaveView | null>(null)
  const [timeline, setTimeline] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const balances = useQuery({ queryKey: ['leave-balances'], queryFn: leaveApi.balances })
  const mine = useQuery({ queryKey: ['leave-mine', page], queryFn: () => leaveApi.mine(page) })
  const refresh = (m: string) => { setToast(m); qc.invalidateQueries({ queryKey: ['leave-mine'] }); qc.invalidateQueries({ queryKey: ['leave-balances'] }) }
  const cancel = useMutation({ mutationFn: (id: string) => leaveApi.cancel(id), onSuccess: (l) => refresh(`${l.reference} cancelled.`), onError: (e) => setToast(errorMessage(e)) })

  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">Leave</Typography>
        {can(Permissions.LeaveApply) && <Button variant="contained" onClick={() => { setEditing(null); setApplying(true) }}>Apply for leave</Button>}
      </Stack>
      {balances.data?.navMessage && <Alert severity="warning">{balances.data.navMessage}</Alert>}
      <Grid container spacing={2}>
        {(balances.data?.balances ?? []).map((b) => (
          <Grid key={b.leaveTypeId} size={{ xs: 6, md: 3 }}>
            <Card><CardContent>
              <Typography variant="body2" color="text.secondary">{b.leaveType}</Typography>
              <Typography variant="h4">{b.available}</Typography>
              <Typography variant="caption" color="text.secondary">of {b.entitlement + b.broughtForward} days · {b.taken} taken · {b.pending} pending</Typography>
            </CardContent></Card>
          </Grid>
        ))}
      </Grid>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}>
        <Tab label="My applications" />
        <Tab label="Calendar" />
      </Tabs>
      {tab === 0 && (
        <Card><CardContent>
          {mine.isLoading ? <LoadingState /> : mine.isError ? <ErrorState error={mine.error} onRetry={() => mine.refetch()} /> : mine.data!.items.length === 0 ? <EmptyState title="No leave applications yet" /> : (
            <>
              <TableContainer>
                <Table size="small">
                  <TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Type</TableCell><TableCell>Dates</TableCell><TableCell align="right">Days</TableCell><TableCell>Status</TableCell><TableCell /></TableRow></TableHead>
                  <TableBody>
                    {mine.data!.items.map((l) => (
                      <TableRow key={l.id} hover>
                        <TableCell>{l.reference}</TableCell>
                        <TableCell>{l.leaveType}</TableCell>
                        <TableCell>{fmt(l.startDate)} – {fmt(l.endDate)}</TableCell>
                        <TableCell align="right">{l.days}</TableCell>
                        <TableCell><Statuses status={l.status} navStatus={l.navStatus} navDocumentNo={l.navDocumentNo} /></TableCell>
                        <TableCell align="right">
                          <Box className="flex justify-end gap-1">
                            {l.workflowInstanceId && <Button size="small" onClick={() => setTimeline(l.workflowInstanceId)}>History</Button>}
                            {l.status === 'ReturnedForCorrection' && <Button size="small" onClick={() => { setEditing(l); setApplying(true) }}>Correct</Button>}
                            {['Submitted', 'InApproval', 'ReturnedForCorrection', 'Approved'].includes(l.status) && dayjs(l.startDate).isAfter(dayjs(), 'day') && (
                              <Button size="small" color="error" disabled={cancel.isPending} onClick={() => cancel.mutate(l.id)}>Cancel</Button>
                            )}
                          </Box>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <Pagination sx={{ mt: 2 }} count={Math.max(1, Math.ceil(mine.data!.totalCount / mine.data!.pageSize))} page={page} onChange={(_, p) => setPage(p)} />
            </>
          )}
        </CardContent></Card>
      )}
      {tab === 1 && <Calendar />}
      <ApplyDialog open={applying} editing={editing} onClose={() => setApplying(false)} onDone={refresh} />
      <ApprovalDialog instanceId={timeline} onClose={() => setTimeline(null)} />
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
