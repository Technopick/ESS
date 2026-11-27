import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Snackbar, Stack, Tab, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { organizationApi, type DepartmentRow, type OrgRow } from '../../api/admin'
import { errorMessage } from '../../api/client'
import { Permissions, useAuth } from '../../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { EmployeePicker } from '../../components/EmployeePicker'

function RoutingDialog({ dept, all, onClose, onDone }: { dept: DepartmentRow | null; all: DepartmentRow[]; onClose: () => void; onDone: (m: string) => void }) {
  const [head, setHead] = useState<{ id: string | null; name: string | null }>({ id: null, name: null })
  const [parent, setParent] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState<string | null>(null)
  if (dept && loaded !== dept.id) { setLoaded(dept.id); setHead({ id: dept.headEmployeeId, name: dept.head }); setParent(dept.parentId ?? ''); setError(null) }
  const save = useMutation({
    mutationFn: () => organizationApi.setRouting(dept!.id, { headEmployeeId: head.id, parentDepartmentId: parent || null }),
    onSuccess: () => { onDone(`${dept!.name} updated. New approval requests use it straight away.`); setLoaded(null); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={!!dept} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Approval routing for {dept?.name}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <Typography variant="body2" color="text.secondary">NAV dimensions do not record who heads a department or which department it belongs to, so the portal keeps these for "Head of department" approval steps and HR scopes.</Typography>
          <EmployeePicker label="Head of department" value={head.id} valueLabel={head.name} onChange={(id, name) => setHead({ id, name })} />
          <TextField select label="Part of" value={parent} onChange={(e) => setParent(e.target.value)}>
            <MenuItem value="">Top level</MenuItem>
            {all.filter((d) => d.id !== dept?.id).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button></DialogActions>
    </Dialog>
  )
}

function SimpleTable({ rows, nameLabel, showDept }: { rows: OrgRow[]; nameLabel: string; showDept?: boolean }) {
  if (rows.length === 0) return <EmptyState title="Nothing synced from NAV yet" />
  return (
    <TableContainer><Table size="small">
      <TableHead><TableRow><TableCell>{nameLabel}</TableCell><TableCell>NAV code</TableCell>{showDept && <TableCell>Department</TableCell>}<TableCell align="right">Current staff</TableCell><TableCell>Last synced</TableCell></TableRow></TableHead>
      <TableBody>{rows.map((r) => (
        <TableRow key={r.id}><TableCell>{r.name}</TableCell><TableCell>{r.code}</TableCell>{showDept && <TableCell>{r.department ?? '–'}</TableCell>}<TableCell align="right">{r.headcount}</TableCell><TableCell>{dayjs(r.lastSyncedAtUtc).format('D MMM YYYY HH:mm')}</TableCell></TableRow>
      ))}</TableBody>
    </Table></TableContainer>
  )
}

export function OrganizationPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const [tab, setTab] = useState(0)
  const [editing, setEditing] = useState<DepartmentRow | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const depts = useQuery({ queryKey: ['org-departments'], queryFn: organizationApi.departments })
  const positions = useQuery({ queryKey: ['org-positions'], queryFn: organizationApi.positions, enabled: tab === 1 })
  const locations = useQuery({ queryKey: ['org-locations'], queryFn: organizationApi.locations, enabled: tab === 2 })
  return (
    <Stack spacing={3}>
      <div>
        <Typography variant="h1">Organisation</Typography>
        <Typography variant="body2" color="text.secondary">Departments, job titles and locations are created and renamed in NAV, then synced here.</Typography>
      </div>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}><Tab label="Departments" /><Tab label="Job titles" /><Tab label="Locations" /></Tabs>
      <Card><CardContent>
        {tab === 0 && (depts.isLoading ? <LoadingState /> : depts.isError ? <ErrorState error={depts.error} /> : depts.data!.length === 0 ? <EmptyState title="No departments synced from NAV yet" /> : (
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>Department</TableCell><TableCell>NAV code</TableCell><TableCell>Part of</TableCell><TableCell>Head</TableCell><TableCell align="right">Current staff</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>{depts.data!.map((d) => (
              <TableRow key={d.id} hover>
                <TableCell>{d.name}</TableCell><TableCell>{d.code}</TableCell><TableCell>{d.parent ?? '–'}</TableCell>
                <TableCell>{d.head ?? <Typography variant="body2" color="warning.main">Not set</Typography>}</TableCell>
                <TableCell align="right">{d.headcount}</TableCell>
                <TableCell align="right">{can(Permissions.WorkflowConfigure) && <Button size="small" onClick={() => setEditing(d)}>Edit routing</Button>}</TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></TableContainer>
        ))}
        {tab === 1 && (positions.isLoading ? <LoadingState /> : positions.isError ? <ErrorState error={positions.error} /> : <SimpleTable rows={positions.data!} nameLabel="Job title" showDept />)}
        {tab === 2 && (locations.isLoading ? <LoadingState /> : locations.isError ? <ErrorState error={locations.error} /> : <SimpleTable rows={locations.data!} nameLabel="Location" />)}
      </CardContent></Card>
      <RoutingDialog dept={editing} all={depts.data ?? []} onClose={() => setEditing(null)} onDone={(m) => { setToast(m); qc.invalidateQueries({ queryKey: ['org-departments'] }) }} />
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
