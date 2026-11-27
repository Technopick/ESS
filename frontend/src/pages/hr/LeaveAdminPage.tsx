import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router-dom'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardContent, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, Link, MenuItem, Pagination, Snackbar, Stack,
  Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Tooltip, Typography,
} from '@mui/material'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline'
import { leaveAdminApi, statusName, type LeaveTypeRow, type LeaveTypeRules } from '../../api/admin'
import { errorMessage } from '../../api/client'
import { Permissions, useAuth } from '../../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { Statuses } from '../../components/Status'
import { ApprovalDialog } from '../../components/ApprovalDialog'
import { DepartmentSelect } from '../../components/OrgFilters'

function Applications() {
  const [q, setQ] = useState<{ status?: string; departmentId?: string; from?: string; to?: string; search?: string; navStatus?: string; page: number }>({ page: 1 })
  const [search, setSearch] = useState('')
  const [timeline, setTimeline] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['hr-leave-apps', q], queryFn: () => leaveAdminApi.applications(q) })
  const set = (p: object) => setQ({ ...q, page: 1, ...p })
  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} useFlexGap flexWrap="wrap" component="form" onSubmit={(e) => { e.preventDefault(); set({ search }) }}>
        <TextField size="small" label="Search name or reference" value={search} onChange={(e) => setSearch(e.target.value)} />
        <DepartmentSelect value={q.departmentId ?? ''} onChange={(v) => set({ departmentId: v })} />
        <TextField select size="small" label="Status" value={q.status ?? ''} onChange={(e) => set({ status: e.target.value })} sx={{ minWidth: 160 }}>
          <MenuItem value="">Any status</MenuItem>
          {['Submitted', 'InApproval', 'ReturnedForCorrection', 'Approved', 'Rejected', 'Cancelled', 'Withdrawn'].map((s) => <MenuItem key={s} value={s}>{statusName(s)}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="NAV" value={q.navStatus ?? ''} onChange={(e) => set({ navStatus: e.target.value })} sx={{ minWidth: 160 }}>
          <MenuItem value="">Any</MenuItem><MenuItem value="PendingIntegration">Waiting to post</MenuItem><MenuItem value="Posted">Recorded in NAV</MenuItem>
          <MenuItem value="Failed">NAV refused</MenuItem><MenuItem value="RequiresReconciliation">Being checked</MenuItem>
        </TextField>
        <TextField size="small" type="date" label="On leave from" InputLabelProps={{ shrink: true }} value={q.from ?? ''} onChange={(e) => set({ from: e.target.value })} />
        <TextField size="small" type="date" label="to" InputLabelProps={{ shrink: true }} value={q.to ?? ''} onChange={(e) => set({ to: e.target.value })} />
        <Button type="submit" variant="contained">Search</Button>
      </Stack>
      {list.isLoading ? <LoadingState rows={6} /> : list.isError ? <ErrorState error={list.error} /> : list.data!.items.length === 0 ? <EmptyState title="No applications match" /> : (
        <>
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Employee</TableCell><TableCell>Leave</TableCell><TableCell>Dates</TableCell><TableCell align="right">Days</TableCell><TableCell>Status</TableCell><TableCell>Waiting with</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>{list.data!.items.map((a) => (
              <TableRow key={a.id} hover>
                <TableCell>{a.reference}<Typography variant="caption" display="block" color="text.secondary">{a.submittedAtUtc ? dayjs(a.submittedAtUtc).format('D MMM YYYY') : ''}</Typography></TableCell>
                <TableCell><Link component={RouterLink} to={`/hr/employees/${a.employeeId}`}>{a.employee}</Link><Typography variant="caption" display="block" color="text.secondary">{a.department}</Typography></TableCell>
                <TableCell>{a.leaveType}</TableCell>
                <TableCell>{dayjs(a.startDate).format('D MMM')} to {dayjs(a.endDate).format('D MMM YYYY')}</TableCell>
                <TableCell align="right">{a.days}</TableCell>
                <TableCell><Statuses status={a.status} navStatus={a.navStatus} navDocumentNo={a.navDocumentNo} /></TableCell>
                <TableCell>{a.pendingWith ?? '–'}</TableCell>
                <TableCell>{a.workflowInstanceId && <Button size="small" onClick={() => setTimeline(a.workflowInstanceId)}>History</Button>}</TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></TableContainer>
          <Pagination count={Math.max(1, Math.ceil(list.data!.totalCount / list.data!.pageSize))} page={q.page} onChange={(_, p) => setQ({ ...q, page: p })} />
        </>
      )}
      <ApprovalDialog instanceId={timeline} onClose={() => setTimeline(null)} />
    </Stack>
  )
}

function TypeDialog({ type, onClose, onDone }: { type: LeaveTypeRow | null; onClose: () => void; onDone: (m: string) => void }) {
  const [r, setR] = useState<LeaveTypeRules | null>(null)
  const [error, setError] = useState<string | null>(null)
  if (type && (!r || (r as LeaveTypeRules & { _id?: string })._id !== type.id)) {
    setR({ ...type, _id: type.id } as LeaveTypeRules)
    setError(null)
  }
  const save = useMutation({
    mutationFn: () => {
      const { countsWorkingDaysOnly, requiresAttachment, maxDaysPerApplication, minNoticeDays, allowNegativeBalance, allowHalfDay, eligibleGender, isActive } = r!
      return leaveAdminApi.updateType(type!.id, { countsWorkingDaysOnly, requiresAttachment, maxDaysPerApplication: maxDaysPerApplication ? Number(maxDaysPerApplication) : null, minNoticeDays: Number(minNoticeDays) || 0, allowNegativeBalance, allowHalfDay, eligibleGender: eligibleGender || null, isActive })
    },
    onSuccess: () => { onDone(`${type!.name} rules saved.`); setR(null); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const close = () => { setR(null); onClose() }
  return (
    <Dialog open={!!type} onClose={close} fullWidth maxWidth="sm">
      <DialogTitle>{type?.name} rules</DialogTitle>
      {r && <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <Typography variant="body2" color="text.secondary">Entitlement and balances come from NAV. These rules are checked in the portal before an application is sent; NAV checks it again when it is posted.</Typography>
          <Stack direction="row" spacing={2}>
            <TextField fullWidth type="number" label="Minimum notice (days)" value={r.minNoticeDays} onChange={(e) => setR({ ...r, minNoticeDays: e.target.value as unknown as number })} inputProps={{ min: 0 }} />
            <TextField fullWidth type="number" label="Most days per application" value={r.maxDaysPerApplication ?? ''} onChange={(e) => setR({ ...r, maxDaysPerApplication: (e.target.value || null) as unknown as number })} helperText="Empty for no limit" />
          </Stack>
          <TextField label="Only for employees whose NAV gender is" value={r.eligibleGender ?? ''} onChange={(e) => setR({ ...r, eligibleGender: e.target.value })} helperText="For example Female for maternity leave. Leave empty for everyone." />
          <FormControlLabel control={<Checkbox checked={r.countsWorkingDaysOnly} onChange={(e) => setR({ ...r, countsWorkingDaysOnly: e.target.checked })} />} label="Count working days only (skip weekends and holidays)" />
          <FormControlLabel control={<Checkbox checked={r.allowHalfDay} onChange={(e) => setR({ ...r, allowHalfDay: e.target.checked })} />} label="Allow half days" />
          <FormControlLabel control={<Checkbox checked={r.requiresAttachment} onChange={(e) => setR({ ...r, requiresAttachment: e.target.checked })} />} label="Ask for a supporting document" />
          <FormControlLabel control={<Checkbox checked={r.allowNegativeBalance} onChange={(e) => setR({ ...r, allowNegativeBalance: e.target.checked })} />} label="Allow applications beyond the balance" />
          <FormControlLabel control={<Checkbox checked={r.isActive} onChange={(e) => setR({ ...r, isActive: e.target.checked })} />} label="Employees can apply for this leave type" />
        </Stack>
      </DialogContent>}
      <DialogActions><Button onClick={close}>Cancel</Button><Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button></DialogActions>
    </Dialog>
  )
}

function Types({ onToast }: { onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState<LeaveTypeRow | null>(null)
  const types = useQuery({ queryKey: ['hr-leave-types'], queryFn: leaveAdminApi.types })
  return types.isLoading ? <LoadingState /> : types.isError ? <ErrorState error={types.error} /> : (
    <>
      <TableContainer><Table size="small">
        <TableHead><TableRow><TableCell>Leave type</TableCell><TableCell>Counts</TableCell><TableCell align="right">Notice</TableCell><TableCell align="right">Max days</TableCell><TableCell>Other rules</TableCell><TableCell>Available</TableCell><TableCell /></TableRow></TableHead>
        <TableBody>{types.data!.map((t) => (
          <TableRow key={t.id} hover>
            <TableCell>{t.name}<Typography variant="caption" display="block" color="text.secondary">{t.code}</Typography></TableCell>
            <TableCell>{t.countsWorkingDaysOnly ? 'Working days' : 'Calendar days'}</TableCell>
            <TableCell align="right">{t.minNoticeDays} d</TableCell>
            <TableCell align="right">{t.maxDaysPerApplication ?? 'No limit'}</TableCell>
            <TableCell>{[t.allowHalfDay && 'Half days', t.requiresAttachment && 'Document', t.allowNegativeBalance && 'Beyond balance', t.eligibleGender && `${t.eligibleGender} only`].filter(Boolean).join(', ') || '–'}</TableCell>
            <TableCell>{t.isActive ? 'Yes' : 'No'}</TableCell>
            <TableCell align="right"><Button size="small" onClick={() => setEditing(t)}>Edit</Button></TableCell>
          </TableRow>
        ))}</TableBody>
      </Table></TableContainer>
      <TypeDialog type={editing} onClose={() => setEditing(null)} onDone={(m) => { onToast(m); qc.invalidateQueries({ queryKey: ['hr-leave-types'] }) }} />
    </>
  )
}

function Holidays({ canEdit, onToast }: { canEdit: boolean; onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const [year, setYear] = useState(dayjs().year())
  const [form, setForm] = useState({ date: '', name: '' })
  const list = useQuery({ queryKey: ['hr-holidays', year], queryFn: () => leaveAdminApi.holidays(year) })
  const refresh = (m: string) => { onToast(m); qc.invalidateQueries({ queryKey: ['hr-holidays'] }) }
  const add = useMutation({ mutationFn: () => leaveAdminApi.addHoliday(form.date, form.name), onSuccess: () => { setForm({ date: '', name: '' }); refresh('Holiday added.') }, onError: (e) => onToast(errorMessage(e)) })
  const remove = useMutation({ mutationFn: leaveAdminApi.removeHoliday, onSuccess: () => refresh('Holiday removed.'), onError: (e) => onToast(errorMessage(e)) })
  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} useFlexGap flexWrap="wrap" alignItems="center">
        <TextField select size="small" label="Year" value={year} onChange={(e) => setYear(Number(e.target.value))}>{[-1, 0, 1].map((o) => <MenuItem key={o} value={dayjs().year() + o}>{dayjs().year() + o}</MenuItem>)}</TextField>
        {canEdit && <>
          <TextField size="small" type="date" label="Date" InputLabelProps={{ shrink: true }} value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} />
          <TextField size="small" label="Holiday" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Button variant="contained" disabled={!form.date || !form.name.trim() || add.isPending} onClick={() => add.mutate()}>Add holiday</Button>
        </>}
      </Stack>
      <Typography variant="body2" color="text.secondary">Gazetted Kenyan holidays that move (such as Eid al-Fitr) must be added when announced. Holidays from the NAV base calendar are marked and can only be changed in NAV.</Typography>
      {list.isLoading ? <LoadingState /> : list.isError ? <ErrorState error={list.error} /> : list.data!.length === 0 ? <EmptyState title={`No holidays recorded for ${year}`} /> : (
        <Table size="small"><TableBody>{list.data!.map((h) => (
          <TableRow key={h.id}><TableCell sx={{ width: 180 }}>{dayjs(h.date).format('ddd D MMM YYYY')}</TableCell><TableCell>{h.name}{h.fromNav ? ' (NAV)' : ''}</TableCell>
            <TableCell align="right">{canEdit && !h.fromNav && <Tooltip title="Remove"><IconButton aria-label={`Remove ${h.name}`} onClick={() => remove.mutate(h.id)}><DeleteOutlineIcon /></IconButton></Tooltip>}</TableCell></TableRow>
        ))}</TableBody></Table>
      )}
    </Stack>
  )
}

export function LeaveAdminPage() {
  const { can } = useAuth()
  const [tab, setTab] = useState(0)
  const [toast, setToast] = useState<string | null>(null)
  const configure = can(Permissions.LeaveConfigure)
  return (
    <Stack spacing={3}>
      <Typography variant="h1">Leave administration</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}><Tab label="Applications" /><Tab label="Public holidays" />{configure && <Tab label="Leave types" />}</Tabs>
      <Card><CardContent>
        {tab === 0 && <Applications />}
        {tab === 1 && <Holidays canEdit={configure} onToast={setToast} />}
        {tab === 2 && configure && <Types onToast={setToast} />}
      </CardContent></Card>
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
