import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Snackbar, Stack, Tab, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { attendanceApi, type AttendanceRecord } from '../api/modules'
import { errorMessage } from '../api/client'
import { Permissions, useAuth } from '../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { StatusChip } from '../components/Status'

const time = (utc: string | null) => (utc ? dayjs(utc).format('HH:mm') : '–')
const withSeconds = (t: string) => (t ? (t.length === 5 ? `${t}:00` : t) : null)

function position(): Promise<{ latitude: number; longitude: number } | undefined> {
  if (!('geolocation' in navigator)) return Promise.resolve(undefined)
  return new Promise((resolve) => navigator.geolocation.getCurrentPosition(
    (p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }), () => resolve(undefined), { timeout: 8000, maximumAge: 60000 }))
}

function RecordTable({ rows, showEmployee }: { rows: AttendanceRecord[]; showEmployee?: boolean }) {
  return (
    <TableContainer>
      <Table size="small">
        <TableHead><TableRow>{showEmployee ? <TableCell>Employee</TableCell> : <TableCell>Date</TableCell>}<TableCell>In</TableCell><TableCell>Out</TableCell><TableCell align="right">Hours</TableCell><TableCell align="right">Late (min)</TableCell><TableCell>Source</TableCell></TableRow></TableHead>
        <TableBody>
          {rows.map((r) => (
            <TableRow key={r.id}>
              <TableCell>{showEmployee ? r.employeeName : dayjs(r.workDate).format('ddd D MMM')}</TableCell>
              <TableCell>{time(r.clockInUtc)}</TableCell><TableCell>{time(r.clockOutUtc)}</TableCell>
              <TableCell align="right">{r.workedHours.toFixed(2)}</TableCell>
              <TableCell align="right">{r.lateMinutes > 0 ? <Typography variant="body2" color="warning.main">{r.lateMinutes}</Typography> : 0}</TableCell>
              <TableCell>{r.source}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}

function RequestDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const [f, setF] = useState({ type: 'Correction', workDate: dayjs().subtract(1, 'day').format('YYYY-MM-DD'), clockIn: '', clockOut: '', overtimeHours: '', reason: '' })
  const [error, setError] = useState<string | null>(null)
  const save = useMutation({
    mutationFn: () => attendanceApi.request({
      type: f.type, workDate: f.workDate, reason: f.reason,
      clockIn: f.type === 'Overtime' ? null : withSeconds(f.clockIn), clockOut: f.type === 'Overtime' ? null : withSeconds(f.clockOut),
      overtimeHours: f.type === 'Overtime' ? Number(f.overtimeHours) : null,
    }),
    onSuccess: (r) => { onDone(`${r.reference} sent for approval.`); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Attendance request</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <TextField select label="Type" value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })}>
            <MenuItem value="Correction">Correct my times</MenuItem><MenuItem value="MissingPunch">I forgot to clock in or out</MenuItem><MenuItem value="Overtime">Overtime</MenuItem>
          </TextField>
          <TextField type="date" label="Date" InputLabelProps={{ shrink: true }} value={f.workDate} inputProps={{ max: dayjs().format('YYYY-MM-DD') }} onChange={(e) => setF({ ...f, workDate: e.target.value })} />
          {f.type === 'Overtime' ? (
            <TextField type="number" label="Overtime hours" value={f.overtimeHours} inputProps={{ min: 0.5, max: 12, step: 0.5 }} onChange={(e) => setF({ ...f, overtimeHours: e.target.value })} />
          ) : (
            <Stack direction="row" spacing={2}>
              <TextField fullWidth type="time" label="Clock in" InputLabelProps={{ shrink: true }} value={f.clockIn} onChange={(e) => setF({ ...f, clockIn: e.target.value })} />
              <TextField fullWidth type="time" label="Clock out" InputLabelProps={{ shrink: true }} value={f.clockOut} onChange={(e) => setF({ ...f, clockOut: e.target.value })} />
            </Stack>
          )}
          <TextField multiline minRows={2} label="Reason" value={f.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} />
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" disabled={!f.reason.trim() || save.isPending} onClick={() => save.mutate()}>Submit</Button></DialogActions>
    </Dialog>
  )
}

export function AttendancePage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const [tab, setTab] = useState(0)
  const [requesting, setRequesting] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [teamDate, setTeamDate] = useState(dayjs().format('YYYY-MM-DD'))
  const from = dayjs().subtract(30, 'day').format('YYYY-MM-DD')
  const to = dayjs().format('YYYY-MM-DD')
  const today = useQuery({ queryKey: ['attendance-today'], queryFn: attendanceApi.today })
  const history = useQuery({ queryKey: ['attendance-mine', from], queryFn: () => attendanceApi.mine(from, to), enabled: tab === 0 })
  const requests = useQuery({ queryKey: ['attendance-requests'], queryFn: attendanceApi.myRequests, enabled: tab === 1 })
  const team = useQuery({ queryKey: ['attendance-team', teamDate], queryFn: () => attendanceApi.team(teamDate), enabled: tab === 2 })
  const clock = useMutation({
    mutationFn: async (action: 'in' | 'out') => attendanceApi.clock(action, await position()),
    onSuccess: (_, a) => { setToast(a === 'in' ? 'Clocked in.' : 'Clocked out.'); qc.invalidateQueries({ queryKey: ['attendance-today'] }); qc.invalidateQueries({ queryKey: ['attendance-mine'] }) },
    onError: (e) => setToast(errorMessage(e)),
  })
  const t = today.data?.today
  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">Attendance</Typography>
        <Button onClick={() => setRequesting(true)}>Correction or overtime</Button>
      </Stack>
      <Card><CardContent>
        {today.isLoading ? <LoadingState rows={1} /> : today.isError ? <ErrorState error={today.error} /> : (
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3} alignItems={{ sm: 'center' }}>
            <Stack flex={1}>
              <Typography variant="overline">{dayjs().format('dddd D MMMM')}</Typography>
              <Typography variant="h5">{t?.clockInUtc ? (t.clockOutUtc ? `Worked ${t.workedHours.toFixed(2)} hours` : `Clocked in at ${time(t.clockInUtc)}`) : 'Not clocked in'}</Typography>
              <Typography variant="body2" color="text.secondary">{today.data!.shiftName}: {today.data!.shiftStart.slice(0, 5)} to {today.data!.shiftEnd.slice(0, 5)}{t && t.lateMinutes > 0 ? ` · ${t.lateMinutes} min late` : ''}</Typography>
            </Stack>
            {can(Permissions.AttendanceClock) && (
              <Stack direction="row" spacing={1}>
                <Button variant="contained" size="large" disabled={!!t?.clockInUtc || clock.isPending} onClick={() => clock.mutate('in')}>Clock in</Button>
                <Button variant="outlined" size="large" disabled={!t?.clockInUtc || !!t?.clockOutUtc || clock.isPending} onClick={() => clock.mutate('out')}>Clock out</Button>
              </Stack>
            )}
          </Stack>
        )}
      </CardContent></Card>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}>
        <Tab label="Last 30 days" /><Tab label="My requests" />{can(Permissions.AttendanceViewTeam) && <Tab label="My team" />}
      </Tabs>
      <Card><CardContent>
        {tab === 0 && (history.isLoading ? <LoadingState /> : history.isError ? <ErrorState error={history.error} /> : history.data!.length === 0 ? <EmptyState title="No attendance recorded" /> : <RecordTable rows={history.data!} />)}
        {tab === 1 && (requests.isLoading ? <LoadingState /> : requests.isError ? <ErrorState error={requests.error} /> : requests.data!.length === 0 ? <EmptyState title="No requests" /> : (
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Type</TableCell><TableCell>Date</TableCell><TableCell>Details</TableCell><TableCell>Status</TableCell></TableRow></TableHead>
            <TableBody>{requests.data!.map((r) => (
              <TableRow key={r.id}>
                <TableCell>{r.reference}</TableCell><TableCell>{r.type === 'MissingPunch' ? 'Missing punch' : r.type}</TableCell><TableCell>{dayjs(r.workDate).format('D MMM YYYY')}</TableCell>
                <TableCell>{r.type === 'Overtime' ? `${r.overtimeHours} hours` : `${time(r.requestedClockInUtc)} to ${time(r.requestedClockOutUtc)}`}</TableCell>
                <TableCell><StatusChip status={r.status} /></TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></TableContainer>
        ))}
        {tab === 2 && (
          <Stack spacing={2}>
            <TextField size="small" type="date" label="Date" InputLabelProps={{ shrink: true }} value={teamDate} onChange={(e) => setTeamDate(e.target.value)} sx={{ width: 200 }} />
            {team.isLoading ? <LoadingState /> : team.isError ? <ErrorState error={team.error} /> : team.data!.length === 0 ? <EmptyState title="Nobody has clocked in" /> : <RecordTable rows={team.data!} showEmployee />}
          </Stack>
        )}
      </CardContent></Card>
      <RequestDialog open={requesting} onClose={() => setRequesting(false)} onDone={(m) => { setToast(m); qc.invalidateQueries({ queryKey: ['attendance-requests'] }) }} />
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
