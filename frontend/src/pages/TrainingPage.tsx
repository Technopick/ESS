import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardActions, CardContent, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Grid2 as Grid, MenuItem, Rating,
  Snackbar, Stack, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import { money, trainingApi, type TrainingParticipation, type TrainingProgram, type TrainingProgramInput } from '../api/modules'
import { errorMessage } from '../api/client'
import { Permissions, useAuth } from '../auth/AuthContext'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { StatusChip } from '../components/Status'

const when = (s: string | null, e: string | null) => (s ? `${dayjs(s).format('D MMM YYYY, HH:mm')}${e ? ` to ${dayjs(e).format(dayjs(e).isSame(s, 'day') ? 'HH:mm' : 'D MMM YYYY')}` : ''}` : 'Date to be confirmed')
const local = (utc: string | null) => (utc ? dayjs(utc).format('YYYY-MM-DDTHH:mm') : '')
const toUtc = (v: string) => (v ? dayjs(v).toISOString() : null)

const blank: TrainingProgramInput = { code: '', title: '', description: '', provider: '', startsAtUtc: null, endsAtUtc: null, venue: '', capacity: null, costPerParticipant: null, requiresApproval: true, isPublished: true }

function ProgramDialog({ program, open, onClose, onDone }: { program: TrainingProgram | null; open: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const [f, setF] = useState<TrainingProgramInput>(blank)
  const [error, setError] = useState<string | null>(null)
  const [loadedFor, setLoadedFor] = useState<string | null | undefined>(undefined)
  if (open && loadedFor !== (program?.id ?? null)) {
    setLoadedFor(program?.id ?? null)
    setF(program ? { ...program } : blank)
    setError(null)
  }
  const save = useMutation({
    mutationFn: () => {
      const body = { ...f, capacity: f.capacity ? Number(f.capacity) : null, costPerParticipant: f.costPerParticipant ? Number(f.costPerParticipant) : null }
      return program ? trainingApi.update(program.id, body) : trainingApi.create(body).then(() => undefined)
    },
    onSuccess: () => { onDone(program ? 'Programme updated.' : 'Programme created.'); setLoadedFor(undefined); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={open} onClose={() => { setLoadedFor(undefined); onClose() }} fullWidth maxWidth="sm">
      <DialogTitle>{program ? 'Edit programme' : 'New training programme'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <Stack direction="row" spacing={2}>
            <TextField label="Code" value={f.code} onChange={(e) => setF({ ...f, code: e.target.value.toUpperCase() })} sx={{ width: 160 }} />
            <TextField fullWidth label="Title" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} />
          </Stack>
          <TextField multiline minRows={3} label="Description" value={f.description ?? ''} onChange={(e) => setF({ ...f, description: e.target.value })} />
          <Stack direction="row" spacing={2}>
            <TextField fullWidth label="Provider" value={f.provider ?? ''} onChange={(e) => setF({ ...f, provider: e.target.value })} />
            <TextField fullWidth label="Venue" value={f.venue ?? ''} onChange={(e) => setF({ ...f, venue: e.target.value })} />
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField fullWidth type="datetime-local" label="Starts" InputLabelProps={{ shrink: true }} value={local(f.startsAtUtc)} onChange={(e) => setF({ ...f, startsAtUtc: toUtc(e.target.value) })} />
            <TextField fullWidth type="datetime-local" label="Ends" InputLabelProps={{ shrink: true }} value={local(f.endsAtUtc)} onChange={(e) => setF({ ...f, endsAtUtc: toUtc(e.target.value) })} />
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField fullWidth type="number" label="Places" value={f.capacity ?? ''} onChange={(e) => setF({ ...f, capacity: e.target.value as unknown as number })} />
            <TextField fullWidth type="number" label="Cost per person (KES)" value={f.costPerParticipant ?? ''} onChange={(e) => setF({ ...f, costPerParticipant: e.target.value as unknown as number })} />
          </Stack>
          <FormControlLabel control={<Checkbox checked={f.requiresApproval} onChange={(e) => setF({ ...f, requiresApproval: e.target.checked })} />} label="Applications need approval" />
          <FormControlLabel control={<Checkbox checked={f.isPublished} onChange={(e) => setF({ ...f, isPublished: e.target.checked })} />} label="Show in the catalogue" />
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => { setLoadedFor(undefined); onClose() }}>Cancel</Button><Button variant="contained" disabled={!f.code || !f.title || save.isPending} onClick={() => save.mutate()}>Save</Button></DialogActions>
    </Dialog>
  )
}

function ParticipantsDialog({ program, onClose, onToast }: { program: TrainingProgram | null; onClose: () => void; onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['training-participants', program?.id], queryFn: () => trainingApi.participants(program!.id), enabled: !!program })
  const set = useMutation({
    mutationFn: ({ id, status }: { id: string; status: string }) => trainingApi.setStatus(id, status),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['training-participants'] }),
    onError: (e) => onToast(errorMessage(e)),
  })
  return (
    <Dialog open={!!program} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Participants: {program?.title}</DialogTitle>
      <DialogContent>
        {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} /> : q.data!.length === 0 ? <EmptyState title="Nobody has applied yet" /> : (
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>Employee</TableCell><TableCell>Status</TableCell><TableCell>Feedback</TableCell><TableCell>Set status</TableCell></TableRow></TableHead>
            <TableBody>{q.data!.map((p) => (
              <TableRow key={p.id}>
                <TableCell>{p.employeeName}</TableCell><TableCell><StatusChip status={p.status} /></TableCell>
                <TableCell>{p.feedbackRating ? <Rating size="small" readOnly value={p.feedbackRating} /> : '–'}</TableCell>
                <TableCell>
                  {!['Applied', 'Rejected', 'Cancelled'].includes(p.status) && (
                    <TextField select size="small" value="" onChange={(e) => set.mutate({ id: p.id, status: e.target.value })} sx={{ width: 160 }} label="Change to">
                      {['Registered', 'Attended', 'Completed', 'NoShow'].filter((s) => s !== p.status).map((s) => <MenuItem key={s} value={s}>{s === 'NoShow' ? 'Did not attend' : s}</MenuItem>)}
                    </TextField>
                  )}
                </TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></TableContainer>
        )}
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Close</Button></DialogActions>
    </Dialog>
  )
}

export function TrainingPage() {
  const { can } = useAuth()
  const manage = can(Permissions.TrainingManage)
  const qc = useQueryClient()
  const [tab, setTab] = useState(0)
  const [toast, setToast] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ program: TrainingProgram | null } | null>(null)
  const [people, setPeople] = useState<TrainingProgram | null>(null)
  const [feedback, setFeedback] = useState<{ item: TrainingParticipation; rating: number; comment: string } | null>(null)
  const programs = useQuery({ queryKey: ['training-programs', manage], queryFn: () => trainingApi.programs(manage) })
  const mine = useQuery({ queryKey: ['training-mine'], queryFn: trainingApi.mine, enabled: tab === 1 })
  const refresh = (m: string) => { setToast(m); qc.invalidateQueries({ queryKey: ['training-programs'] }); qc.invalidateQueries({ queryKey: ['training-mine'] }) }
  const apply = useMutation({ mutationFn: trainingApi.apply, onSuccess: () => refresh('Application sent.'), onError: (e) => setToast(errorMessage(e)) })
  const withdraw = useMutation({ mutationFn: trainingApi.withdraw, onSuccess: () => refresh('Withdrawn.'), onError: (e) => setToast(errorMessage(e)) })
  const sendFeedback = useMutation({
    mutationFn: () => trainingApi.feedback(feedback!.item.id, feedback!.rating, feedback!.comment || undefined),
    onSuccess: () => { setFeedback(null); refresh('Thank you for your feedback.') }, onError: (e) => setToast(errorMessage(e)),
  })
  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">Training</Typography>
        {manage && <Button variant="contained" onClick={() => setEditing({ program: null })}>New programme</Button>}
      </Stack>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}><Tab label="Catalogue" /><Tab label="My training" /></Tabs>
      {tab === 0 && (programs.isLoading ? <LoadingState /> : programs.isError ? <ErrorState error={programs.error} /> : programs.data!.length === 0 ? <Card><CardContent><EmptyState title="No upcoming training" /></CardContent></Card> : (
        <Grid container spacing={2}>
          {programs.data!.map((p) => {
            const full = p.capacity != null && p.taken >= p.capacity
            return (
              <Grid key={p.id} size={{ xs: 12, md: 6, lg: 4 }}>
                <Card sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
                  <CardContent sx={{ flex: 1 }}>
                    <Stack direction="row" justifyContent="space-between" spacing={1}>
                      <Typography variant="h6">{p.title}</Typography>
                      {p.myStatus && <StatusChip status={p.myStatus} />}
                    </Stack>
                    <Typography variant="body2" color="text.secondary">{when(p.startsAtUtc, p.endsAtUtc)}{p.venue ? ` · ${p.venue}` : ''}</Typography>
                    {p.description && <Typography variant="body2" sx={{ mt: 1 }}>{p.description}</Typography>}
                    <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>
                      {p.provider ? `${p.provider} · ` : ''}{p.capacity != null ? `${Math.max(0, p.capacity - p.taken)} of ${p.capacity} places left` : `${p.taken} registered`}
                      {manage && p.costPerParticipant ? ` · ${money(p.costPerParticipant)} each` : ''}{manage && !p.isPublished ? ' · hidden' : ''}
                    </Typography>
                  </CardContent>
                  <CardActions>
                    {can(Permissions.TrainingApply) && (!p.myStatus || ['Rejected', 'Cancelled'].includes(p.myStatus)) && (
                      <Button disabled={full || apply.isPending} onClick={() => apply.mutate(p.id)}>{full ? 'Full' : p.requiresApproval ? 'Apply' : 'Register'}</Button>
                    )}
                    {manage && <Button onClick={() => setEditing({ program: p })}>Edit</Button>}
                    {manage && <Button onClick={() => setPeople(p)}>Participants</Button>}
                  </CardActions>
                </Card>
              </Grid>
            )
          })}
        </Grid>
      ))}
      {tab === 1 && <Card><CardContent>
        {mine.isLoading ? <LoadingState /> : mine.isError ? <ErrorState error={mine.error} /> : mine.data!.length === 0 ? <EmptyState title="You have not applied for any training" /> : (
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>Programme</TableCell><TableCell>When</TableCell><TableCell>Status</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>{mine.data!.map((t) => (
              <TableRow key={t.id}>
                <TableCell>{t.program}<Typography variant="caption" display="block" color="text.secondary">{t.reference}</Typography></TableCell>
                <TableCell>{when(t.startsAtUtc, t.endsAtUtc)}</TableCell>
                <TableCell><StatusChip status={t.status} /></TableCell>
                <TableCell align="right">
                  {['Applied', 'Approved', 'Registered'].includes(t.status) && <Button size="small" color="error" onClick={() => withdraw.mutate(t.id)}>Withdraw</Button>}
                  {['Attended', 'Completed'].includes(t.status) && (t.feedbackRating ? <Rating size="small" readOnly value={t.feedbackRating} /> : <Button size="small" onClick={() => setFeedback({ item: t, rating: 4, comment: '' })}>Give feedback</Button>)}
                </TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></TableContainer>
        )}
      </CardContent></Card>}
      <ProgramDialog program={editing?.program ?? null} open={!!editing} onClose={() => setEditing(null)} onDone={refresh} />
      <ParticipantsDialog program={people} onClose={() => setPeople(null)} onToast={setToast} />
      <Dialog open={!!feedback} onClose={() => setFeedback(null)} fullWidth maxWidth="xs">
        <DialogTitle>How was {feedback?.item.program}?</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <Rating value={feedback?.rating ?? 0} onChange={(_, v) => feedback && setFeedback({ ...feedback, rating: v ?? 1 })} />
            <TextField multiline minRows={3} label="Comments (optional)" value={feedback?.comment ?? ''} onChange={(e) => feedback && setFeedback({ ...feedback, comment: e.target.value })} />
          </Stack>
        </DialogContent>
        <DialogActions><Button onClick={() => setFeedback(null)}>Cancel</Button><Button variant="contained" disabled={sendFeedback.isPending} onClick={() => sendFeedback.mutate()}>Send</Button></DialogActions>
      </Dialog>
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
