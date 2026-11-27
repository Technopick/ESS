import { useState } from 'react'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardContent, Chip, FormControlLabel, Grid2 as Grid, Link, MenuItem, Pagination, Snackbar, Stack, Switch, Tab, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import { hrEmployeesApi, statusName, type EmployeeQuery, type EmploymentStatus } from '../../api/admin'
import { errorMessage } from '../../api/client'
import { Permissions, useAuth } from '../../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { DepartmentSelect, LocationSelect } from '../../components/OrgFilters'
import { StatusChip } from '../../components/Status'

const date = (d: string | null) => (d ? dayjs(d).format('D MMM YYYY') : '–')
const statusColor = (s: EmploymentStatus) => (s === 'Active' ? 'success' : s === 'OnProbation' ? 'info' : s === 'Terminated' ? 'default' : 'warning')

export function HrEmployeesPage() {
  const navigate = useNavigate()
  const [q, setQ] = useState<EmployeeQuery>({ status: 'Active', page: 1 })
  const [search, setSearch] = useState('')
  const list = useQuery({ queryKey: ['hr-employees', q], queryFn: () => hrEmployeesApi.list(q) })
  const set = (patch: Partial<EmployeeQuery>) => setQ({ ...q, page: 1, ...patch })
  return (
    <Stack spacing={3}>
      <div>
        <Typography variant="h1">Employees</Typography>
        <Typography variant="body2" color="text.secondary">Employee records are kept in NAV and synced here. Changes go through change requests and NAV.</Typography>
      </div>
      <Stack direction="row" spacing={2} useFlexGap flexWrap="wrap" alignItems="center" component="form" onSubmit={(e) => { e.preventDefault(); set({ search }) }}>
        <TextField size="small" label="Search name, number or email" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ minWidth: 260 }} />
        <DepartmentSelect value={q.departmentId ?? ''} onChange={(v) => set({ departmentId: v })} />
        <LocationSelect value={q.locationId ?? ''} onChange={(v) => set({ locationId: v })} />
        <TextField select size="small" label="Status" value={q.status ?? ''} onChange={(e) => set({ status: e.target.value })} sx={{ minWidth: 150 }}>
          <MenuItem value="">Any status</MenuItem>
          {['Active', 'OnProbation', 'Suspended', 'Inactive', 'Terminated'].map((s) => <MenuItem key={s} value={s}>{statusName(s)}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="Sort by" value={q.sort ?? 'name'} onChange={(e) => set({ sort: e.target.value })} sx={{ minWidth: 150 }}>
          <MenuItem value="name">Name</MenuItem><MenuItem value="no">Employee number</MenuItem><MenuItem value="employed">Newest first</MenuItem><MenuItem value="contract">Contract end</MenuItem>
        </TextField>
        <FormControlLabel control={<Switch checked={!!q.contractEndingSoon} onChange={(e) => set({ contractEndingSoon: e.target.checked })} />} label="Contract ends in 90 days" />
        <FormControlLabel control={<Switch checked={!!q.withoutAccount} onChange={(e) => set({ withoutAccount: e.target.checked })} />} label="No portal account" />
        <Button type="submit" variant="contained">Search</Button>
      </Stack>
      <Card><CardContent>
        {list.isLoading ? <LoadingState rows={6} /> : list.isError ? <ErrorState error={list.error} onRetry={() => list.refetch()} /> : list.data!.items.length === 0 ? <EmptyState title="No employees match" /> : (
          <>
            <TableContainer>
              <Table size="small">
                <TableHead><TableRow><TableCell>Employee</TableCell><TableCell>Position</TableCell><TableCell>Department</TableCell><TableCell>Location</TableCell><TableCell>Manager</TableCell><TableCell>Employed</TableCell><TableCell>Contract ends</TableCell><TableCell>Status</TableCell></TableRow></TableHead>
                <TableBody>
                  {list.data!.items.map((e) => (
                    <TableRow key={e.id} hover sx={{ cursor: 'pointer' }} onClick={() => navigate(`/hr/employees/${e.id}`)}>
                      <TableCell><Link component={RouterLink} to={`/hr/employees/${e.id}`} onClick={(ev) => ev.stopPropagation()}>{e.name}</Link>
                        <Typography variant="caption" display="block" color="text.secondary">{e.employeeNo}{e.hasAccount ? '' : ' · no portal account'}</Typography></TableCell>
                      <TableCell>{e.position ?? '–'}</TableCell><TableCell>{e.department ?? '–'}</TableCell><TableCell>{e.location ?? '–'}</TableCell><TableCell>{e.manager ?? '–'}</TableCell>
                      <TableCell>{date(e.employmentDate)}</TableCell><TableCell>{date(e.contractEndDate)}</TableCell>
                      <TableCell><Chip size="small" color={statusColor(e.status)} label={statusName(e.status)} /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mt: 2 }}>
              <Typography variant="body2" color="text.secondary">{list.data!.totalCount} employees</Typography>
              <Pagination count={Math.max(1, Math.ceil(list.data!.totalCount / list.data!.pageSize))} page={q.page ?? 1} onChange={(_, p) => setQ({ ...q, page: p })} />
            </Stack>
          </>
        )}
      </CardContent></Card>
    </Stack>
  )
}

function Section({ title, data }: { title: string; data: Record<string, string | null> | null | undefined }) {
  const entries = Object.entries(data ?? {}).filter(([, v]) => v)
  if (entries.length === 0) return null
  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="subtitle2" gutterBottom>{title}</Typography>
      <Grid container spacing={1}>
        {entries.map(([k, v]) => (
          <Grid key={k} size={{ xs: 12, sm: 6, md: 4 }}>
            <Typography variant="caption" color="text.secondary">{k.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase())}</Typography>
            <Typography variant="body2">{v}</Typography>
          </Grid>
        ))}
      </Grid>
    </Box>
  )
}

function ListSection({ title, rows }: { title: string; rows: Record<string, string | null>[] | undefined }) {
  if (!rows || rows.length === 0) return null
  const cols = Array.from(new Set(rows.flatMap((r) => Object.keys(r))))
  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="subtitle2" gutterBottom>{title}</Typography>
      <Table size="small"><TableHead><TableRow>{cols.map((c) => <TableCell key={c}>{c.replace(/([A-Z])/g, ' $1').replace(/^./, (x) => x.toUpperCase())}</TableCell>)}</TableRow></TableHead>
        <TableBody>{rows.map((r, i) => <TableRow key={i}>{cols.map((c) => <TableCell key={c}>{r[c] ?? ''}</TableCell>)}</TableRow>)}</TableBody></Table>
    </Box>
  )
}

export function HrEmployeeDetailPage() {
  const { id } = useParams()
  const { can } = useAuth()
  const qc = useQueryClient()
  const [tab, setTab] = useState(0)
  const [toast, setToast] = useState<string | null>(null)
  const d = useQuery({ queryKey: ['hr-employee', id], queryFn: () => hrEmployeesApi.get(id!), enabled: !!id })
  const nav = useQuery({ queryKey: ['hr-employee-profile', id], queryFn: () => hrEmployeesApi.profile(id!), enabled: !!id && tab === 1 })
  const refresh = useMutation({
    mutationFn: () => hrEmployeesApi.refresh(id!),
    onSuccess: () => { setToast('Record refreshed from NAV.'); qc.invalidateQueries({ queryKey: ['hr-employee', id] }) },
    onError: (e) => setToast(errorMessage(e)),
  })
  if (d.isLoading) return <LoadingState rows={8} />
  if (d.isError) return <ErrorState error={d.error} onRetry={() => d.refetch()} />
  const x = d.data!
  const e = x.employee
  return (
    <Stack spacing={3}>
      <div>
        <Button component={RouterLink} to="/hr/employees" startIcon={<ArrowBackIcon />} sx={{ mb: 1 }}>Employees</Button>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
          <div>
            <Typography variant="h1">{e.name}</Typography>
            <Typography variant="body2" color="text.secondary">{e.employeeNo} · {e.position ?? 'No position'} · {e.department ?? 'No department'} · {e.location ?? 'No location'}</Typography>
          </div>
          <Stack direction="row" spacing={1} alignItems="center">
            <Chip color={statusColor(e.status)} label={statusName(e.status)} />
            <Button variant="outlined" onClick={() => refresh.mutate()} disabled={refresh.isPending}>{refresh.isPending ? 'Refreshing…' : 'Refresh from NAV'}</Button>
          </Stack>
        </Stack>
        <Typography variant="caption" color="text.secondary">Last synced from NAV {dayjs(e.lastSyncedAtUtc).format('D MMM YYYY HH:mm')}</Typography>
      </div>
      {x.pendingProfileChanges > 0 && <Alert severity="info">{x.pendingProfileChanges} profile change request(s) are waiting for approval. {can(Permissions.ApprovalsAct) && <Link component={RouterLink} to="/approvals">Open approvals</Link>}</Alert>}
      {x.expiringDocuments > 0 && <Alert severity="warning">{x.expiringDocuments} document(s) have expired or expire within 60 days.</Alert>}
      <Tabs value={tab} onChange={(_, v) => setTab(v)}><Tab label="Overview" /><Tab label="NAV profile" /><Tab label="Activity" /></Tabs>
      {tab === 0 && (
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, md: 6 }}><Card sx={{ height: '100%' }}><CardContent>
            <Typography variant="subtitle2" gutterBottom>Employment</Typography>
            <Table size="small"><TableBody>
              {[['Employed since', date(e.employmentDate)], ['Contract ends', date(e.contractEndDate)], ['Left', date(x.terminationDate)], ['Line manager', e.manager ?? '–'],
                ['Work email', x.workEmail ?? '–'], ['Mobile (NAV)', x.mobilePhone ?? '–'], ['Gender (NAV)', x.gender ?? '–']].map(([k, v]) => (
                <TableRow key={k}><TableCell sx={{ color: 'text.secondary', width: '40%' }}>{k}</TableCell><TableCell>{v}</TableCell></TableRow>
              ))}
            </TableBody></Table>
            {x.directReports.length > 0 && (
              <Box sx={{ mt: 2 }}>
                <Typography variant="subtitle2" gutterBottom>Direct reports ({x.directReports.length})</Typography>
                <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">{x.directReports.map((r) => <Chip key={r.id} component={RouterLink} to={`/hr/employees/${r.id}`} clickable label={r.name} />)}</Stack>
              </Box>
            )}
          </CardContent></Card></Grid>
          <Grid size={{ xs: 12, md: 6 }}><Card sx={{ height: '100%' }}><CardContent>
            <Typography variant="subtitle2" gutterBottom>Portal account</Typography>
            {x.account ? (
              <Table size="small"><TableBody>
                {[['User name', x.account.userName], ['Email', x.account.email], ['Status', x.account.isActive ? 'Active' : 'Disabled'], ['Two-step sign-in', x.account.mfaEnabled ? 'On' : 'Off'],
                  ['Last sign-in', x.account.lastLoginAtUtc ? dayjs(x.account.lastLoginAtUtc).format('D MMM YYYY HH:mm') : 'Never'], ['Roles', x.account.roles.join(', ') || '–']].map(([k, v]) => (
                  <TableRow key={k}><TableCell sx={{ color: 'text.secondary', width: '40%' }}>{k}</TableCell><TableCell>{v}</TableCell></TableRow>
                ))}
              </TableBody></Table>
            ) : <Typography variant="body2" color="text.secondary">No portal account. {can(Permissions.UsersManage) ? <Link component={RouterLink} to="/admin/users">Create one under Users.</Link> : 'Ask an administrator to create one.'}</Typography>}
            <Typography variant="subtitle2" sx={{ mt: 2 }} gutterBottom>Leave balances (from NAV)</Typography>
            {x.balances.length === 0 ? <Typography variant="body2" color="text.secondary">No balances have been read from NAV yet.</Typography> : (
              <Table size="small"><TableHead><TableRow><TableCell>Leave type</TableCell><TableCell align="right">Entitled</TableCell><TableCell align="right">Taken</TableCell><TableCell align="right">Pending</TableCell><TableCell align="right">Available</TableCell></TableRow></TableHead>
                <TableBody>{x.balances.map((b) => <TableRow key={b.leaveType + b.period}><TableCell>{b.leaveType}</TableCell><TableCell align="right">{b.entitlement}</TableCell><TableCell align="right">{b.taken}</TableCell><TableCell align="right">{b.pending}</TableCell><TableCell align="right">{b.available}</TableCell></TableRow>)}</TableBody></Table>
            )}
            <Typography variant="body2" sx={{ mt: 2 }}>{x.documents} document(s) on file. {can(Permissions.DocumentsManage) && <Link component={RouterLink} to={`/hr/documents?employeeId=${e.id}`}>Open documents</Link>}</Typography>
          </CardContent></Card></Grid>
        </Grid>
      )}
      {tab === 1 && <Card><CardContent>
        {nav.isLoading ? <LoadingState rows={6} /> : nav.isError ? <ErrorState error={nav.error} onRetry={() => nav.refetch()} /> : !nav.data!.available ? <Alert severity="info">{nav.data!.message}</Alert> : (
          <>
            <Alert severity="info" variant="outlined" sx={{ mb: 2 }}>Read live from NAV. This view is recorded in the audit log. Bank details are masked.</Alert>
            <Section title="Personal details" data={nav.data!.profile?.personalDetails} />
            <Section title="Address" data={nav.data!.profile?.address} />
            <ListSection title="Emergency contacts" rows={nav.data!.profile?.emergencyContacts} />
            <ListSection title="Next of kin" rows={nav.data!.profile?.nextOfKin} />
            <ListSection title="Dependants" rows={nav.data!.profile?.dependants} />
            <ListSection title="Qualifications" rows={nav.data!.profile?.qualifications} />
            <Section title="Bank details" data={nav.data!.profile?.bankDetailsMasked} />
          </>
        )}
      </CardContent></Card>}
      {tab === 2 && <Card><CardContent>
        {x.activity.length === 0 ? <EmptyState title="No portal activity yet" /> : (
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>When</TableCell><TableCell>Kind</TableCell><TableCell>Reference</TableCell><TableCell>Summary</TableCell><TableCell>Status</TableCell></TableRow></TableHead>
            <TableBody>{x.activity.map((a) => (
              <TableRow key={a.kind + a.id}><TableCell>{a.atUtc ? dayjs(a.atUtc).format('D MMM YYYY') : '–'}</TableCell><TableCell>{a.kind}</TableCell><TableCell>{a.reference}</TableCell><TableCell>{a.summary}</TableCell><TableCell><StatusChip status={a.status} /></TableCell></TableRow>
            ))}</TableBody>
          </Table></TableContainer>
        )}
      </CardContent></Card>}
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
