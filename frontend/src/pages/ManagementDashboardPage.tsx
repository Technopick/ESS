import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router-dom'
import dayjs from 'dayjs'
import { Alert, Card, CardContent, Grid2 as Grid, Link, List, ListItem, ListItemText, MenuItem, Snackbar, Stack, TextField, Typography } from '@mui/material'
import { managementApi, type DashboardQuery, type PersonItem } from '../api/admin'
import { money } from '../api/modules'
import { errorMessage } from '../api/client'
import { Permissions, useAuth } from '../auth/AuthContext'
import { ErrorState, LoadingState } from '../components/StateViews'
import { BarList, StatTile, TrendLine } from '../components/Charts'
import { ExportMenu } from '../components/ExportMenu'
import { DepartmentSelect, LocationSelect } from '../components/OrgFilters'

function People({ title, items, empty, canOpen }: { title: string; items: PersonItem[]; empty: string; canOpen: boolean }) {
  return (
    <Card sx={{ height: '100%' }}><CardContent>
      <Typography variant="subtitle2">{title}</Typography>
      {items.length === 0 ? <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>{empty}</Typography> : (
        <List dense disablePadding>
          {items.slice(0, 8).map((p) => (
            <ListItem key={p.employeeId + p.date} disableGutters>
              <ListItemText primary={canOpen ? <Link component={RouterLink} to={`/hr/employees/${p.employeeId}`}>{p.name}</Link> : p.name}
                secondary={`${p.department ?? ''}${p.department ? ' · ' : ''}${dayjs(p.date).format('D MMM')}${p.detail ? ` · ${p.detail}` : ''}`} />
            </ListItem>
          ))}
          {items.length > 8 && <Typography variant="caption" color="text.secondary">and {items.length - 8} more</Typography>}
        </List>
      )}
    </CardContent></Card>
  )
}

export function ManagementDashboardPage() {
  const { can } = useAuth()
  const [q, setQ] = useState<DashboardQuery>({ from: dayjs().subtract(12, 'month').add(1, 'day').format('YYYY-MM-DD'), to: dayjs().format('YYYY-MM-DD') })
  const [toast, setToast] = useState<string | null>(null)
  const d = useQuery({ queryKey: ['management', q], queryFn: () => managementApi.dashboard(q) })
  const set = (patch: Partial<DashboardQuery>) => setQ({ ...q, ...patch })
  const data = d.data
  return (
    <Stack spacing={3}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
        <div>
          <Typography variant="h1">Management dashboard</Typography>
          {data && <Typography variant="body2" color="text.secondary">{data.scopeLabel}. Staff figures are as of today; activity covers the period.</Typography>}
        </div>
        {can(Permissions.DataExport) && <ExportMenu csv={false} onExport={(f) => managementApi.export(q, f)} onError={(e) => setToast(errorMessage(e))} disabled={!data} />}
      </Stack>
      <Stack direction="row" spacing={2} useFlexGap flexWrap="wrap" component="form" aria-label="Filters">
        <DepartmentSelect value={q.departmentId ?? ''} onChange={(v) => set({ departmentId: v })} />
        <LocationSelect value={q.locationId ?? ''} onChange={(v) => set({ locationId: v })} />
        <TextField select size="small" label="Employment status" value={q.status ?? ''} onChange={(e) => set({ status: e.target.value })} sx={{ minWidth: 170 }}>
          <MenuItem value="">All statuses</MenuItem>
          {['Active', 'OnProbation', 'Suspended', 'Inactive', 'Terminated'].map((s) => <MenuItem key={s} value={s}>{s === 'OnProbation' ? 'On probation' : s}</MenuItem>)}
        </TextField>
        <TextField size="small" type="date" label="From" InputLabelProps={{ shrink: true }} value={q.from ?? ''} onChange={(e) => set({ from: e.target.value })} />
        <TextField size="small" type="date" label="To" InputLabelProps={{ shrink: true }} value={q.to ?? ''} onChange={(e) => set({ to: e.target.value })} />
      </Stack>
      {d.isLoading ? <LoadingState rows={6} /> : d.isError ? <ErrorState error={d.error} onRetry={() => d.refetch()} /> : data && (
        <>
          <Grid container spacing={2}>
            <Grid size={{ xs: 6, md: 3 }}><StatTile label="Employees" value={data.totalEmployees} hint={`${data.activeEmployees} active`} /></Grid>
            <Grid size={{ xs: 6, md: 3 }}><StatTile label="On leave today" value={data.onLeaveToday} /></Grid>
            <Grid size={{ xs: 6, md: 3 }}><StatTile label="Waiting for approval" value={data.pendingLeave + data.pendingExpenses} hint={`${data.pendingLeave} leave · ${data.pendingExpenses} claims (${money(data.pendingExpenseAmount)})`} /></Grid>
            <Grid size={{ xs: 6, md: 3 }}><StatTile label="Staff turnover" value={data.turnoverPercent == null ? 'No data' : `${data.turnoverPercent}%`} hint={`${data.joiners} joined · ${data.leavers} left in the period`} /></Grid>
          </Grid>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, md: 6 }}><Card><CardContent><TrendLine title="Headcount by month" data={data.headcountByMonth} valueLabel="Staff" /></CardContent></Card></Grid>
            <Grid size={{ xs: 12, md: 6 }}><Card><CardContent><TrendLine title="Approved leave days by month" data={data.leaveDaysByMonth} valueLabel="Days" /></CardContent></Card></Grid>
          </Grid>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, md: 4 }}><Card sx={{ height: '100%' }}><CardContent><BarList title="Staff by department" data={data.byDepartment} /></CardContent></Card></Grid>
            <Grid size={{ xs: 12, md: 4 }}><Card sx={{ height: '100%' }}><CardContent><BarList title="Staff by location" data={data.byLocation} /></CardContent></Card></Grid>
            <Grid size={{ xs: 12, md: 4 }}><Card sx={{ height: '100%' }}><CardContent><BarList title="Employees by status" data={data.byStatus} valueLabel="Employees" /></CardContent></Card></Grid>
          </Grid>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, md: 3 }}><People title="On leave today" items={data.onLeave} empty="Nobody is on approved leave today." canOpen={can(Permissions.EmployeesViewAll)} /></Grid>
            <Grid size={{ xs: 12, md: 3 }}><People title="Contracts ending in 90 days" items={data.contractExpiries} empty="No fixed-term contracts end soon." canOpen={can(Permissions.EmployeesViewAll)} /></Grid>
            <Grid size={{ xs: 12, md: 3 }}><People title="Work anniversaries (30 days)" items={data.anniversaries} empty="None in the next 30 days." canOpen={can(Permissions.EmployeesViewAll)} /></Grid>
            <Grid size={{ xs: 12, md: 3 }}>{data.birthdaysShown
              ? <People title="Birthdays (30 days)" items={data.birthdays} empty="None in the next 30 days." canOpen={can(Permissions.EmployeesViewAll)} />
              : <Card sx={{ height: '100%' }}><CardContent><Typography variant="subtitle2">Birthdays</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>Hidden by the company privacy setting.</Typography></CardContent></Card>}</Grid>
          </Grid>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, md: 6 }}><Card sx={{ height: '100%' }}><CardContent>
              <Grid container spacing={2} sx={{ mb: 2 }}>
                <Grid size={4}><StatTile label="Training participants" value={data.training.participants} hint={`${data.training.programmes} programmes`} /></Grid>
                <Grid size={4}><StatTile label="Completed" value={data.training.completed} hint={`${data.training.noShows} did not attend`} /></Grid>
                <Grid size={4}><StatTile label="Average rating" value={data.training.averageRating == null ? 'None yet' : `${data.training.averageRating} / 5`} /></Grid>
              </Grid>
              <BarList title="Training participation by status" data={data.training.byStatus} valueLabel="Participants" />
            </CardContent></Card></Grid>
            <Grid size={{ xs: 12, md: 6 }}><Card sx={{ height: '100%' }}><CardContent>
              <Grid container spacing={2} sx={{ mb: 2 }}>
                <Grid size={4}><StatTile label="HR requests" value={data.requests.submitted} hint={`${data.requests.open} still open`} /></Grid>
                <Grid size={4}><StatTile label="Within SLA" value={data.requests.withinSlaPercent == null ? 'No data' : `${data.requests.withinSlaPercent}%`} /></Grid>
                <Grid size={4}><StatTile label="Average time to resolve" value={data.requests.averageHoursToResolve == null ? 'No data' : `${data.requests.averageHoursToResolve} h`} /></Grid>
              </Grid>
              <BarList title="HR requests by type" data={data.requests.byType} valueLabel="Requests" />
            </CardContent></Card></Grid>
          </Grid>
          <Alert severity="info" variant="outlined">Figures come from the portal's copy of NAV data, refreshed by the scheduled sync, and from requests made in the portal. Pay is never shown here.</Alert>
        </>
      )}
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
