import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, Autocomplete, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, FormControlLabel, IconButton,
  MenuItem, Paper, Snackbar, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline'
import { approvalsApi, documentTypeLabel, workflowAdminApi, type WorkflowRoute, type WorkflowStep } from '../../api/modules'
import { errorMessage } from '../../api/client'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'

const approverLabel: Record<WorkflowStep['approverType'], string> = {
  LineManager: 'Line manager', DepartmentHead: 'Head of department', ManagerOfManager: "Manager's manager", Role: 'Anyone with a role', SpecificUser: 'A named person',
}
const newStep = (sequence: number): WorkflowStep => ({
  sequence, name: '', approverType: 'LineManager', approverRole: null, approverUserId: null, mode: 'AnyOne', minAmount: null, maxAmount: null,
  deadlineHours: 48, reminderEveryHours: 24, escalateToRole: null, skipIfApproverIsRequester: true,
})
const num = (v: string) => (v === '' ? null : Number(v))

function describe(s: WorkflowStep) {
  const who = s.approverType === 'Role' ? `${s.approverRole ?? 'role'}` : s.approverType === 'SpecificUser' ? 'a named person' : approverLabel[s.approverType].toLowerCase()
  const amount = s.minAmount != null || s.maxAmount != null ? ` when the amount is ${s.minAmount != null ? `at least ${s.minAmount.toLocaleString()}` : ''}${s.minAmount != null && s.maxAmount != null ? ' and ' : ''}${s.maxAmount != null ? `at most ${s.maxAmount.toLocaleString()}` : ''}` : ''
  return `${who}${amount}${s.deadlineHours ? `, within ${s.deadlineHours} h` : ''}`
}

function UserPicker({ value, onChange }: { value: string | null; onChange: (id: string | null) => void }) {
  const [search, setSearch] = useState('')
  const q = useQuery({ queryKey: ['approvers', search], queryFn: () => approvalsApi.approvers(search || undefined) })
  const options = q.data ?? []
  return (
    <Autocomplete size="small" sx={{ minWidth: 220 }} options={options} getOptionLabel={(o) => o.name} isOptionEqualToValue={(a, b) => a.userId === b.userId}
      value={options.find((o) => o.userId === value) ?? null} onChange={(_, v) => onChange(v?.userId ?? null)} onInputChange={(_, v) => setSearch(v)}
      renderInput={(p) => <TextField {...p} label={value && !options.some((o) => o.userId === value) ? 'Person (saved)' : 'Person'} />} />
  )
}

function RouteEditor({ route, roles, onClose, onDone }: { route: WorkflowRoute | null; roles: string[]; onClose: () => void; onDone: (m: string) => void }) {
  const [draft, setDraft] = useState<WorkflowRoute | null>(null)
  const [error, setError] = useState<string | null>(null)
  if (route && draft?.documentType !== route.documentType) { setDraft(structuredClone(route)); setError(null) }
  const set = (i: number, patch: Partial<WorkflowStep>) => draft && setDraft({ ...draft, steps: draft.steps.map((s, n) => (n === i ? { ...s, ...patch } : s)) })
  const save = useMutation({
    mutationFn: () => workflowAdminApi.save(draft!.documentType, { name: draft!.name, owner: draft!.owner, steps: draft!.steps }),
    onSuccess: () => { onDone(`Approval route for ${documentTypeLabel(draft!.documentType)} saved. Requests already in progress keep their old route.`); setDraft(null); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const close = () => { setDraft(null); onClose() }
  return (
    <Dialog open={!!route} onClose={close} fullWidth maxWidth="lg">
      <DialogTitle>{draft ? `${documentTypeLabel(draft.documentType)} approval route` : ''}</DialogTitle>
      {draft && (
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
            <Stack direction="row" spacing={2}>
              <TextField fullWidth label="Route name" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
              <TextField select label="Approved in" value={draft.owner} onChange={(e) => setDraft({ ...draft, owner: e.target.value as WorkflowRoute['owner'] })} sx={{ width: 220 }}>
                <MenuItem value="Portal">The portal</MenuItem><MenuItem value="Nav">NAV (its own approval)</MenuItem>
              </TextField>
            </Stack>
            {draft.owner === 'Nav' ? <Alert severity="info">The portal will send these requests straight to NAV and show NAV's approval status. No portal steps are used.</Alert> : (
              <>
                <Typography variant="body2" color="text.secondary">Steps run in order. Steps with the same number run at the same time. Nobody is ever asked to approve their own request.</Typography>
                {draft.steps.map((s, i) => (
                  <Paper key={i} variant="outlined" sx={{ p: 2 }}>
                    <Stack spacing={2}>
                      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }}>
                        <TextField size="small" type="number" label="Step" value={s.sequence} onChange={(e) => set(i, { sequence: Number(e.target.value) || 1 })} sx={{ width: 80 }} inputProps={{ min: 1 }} />
                        <TextField size="small" label="Name" value={s.name} onChange={(e) => set(i, { name: e.target.value })} sx={{ flex: 1 }} />
                        <TextField size="small" select label="Approver" value={s.approverType} onChange={(e) => set(i, { approverType: e.target.value as WorkflowStep['approverType'] })} sx={{ width: 220 }}>
                          {Object.entries(approverLabel).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
                        </TextField>
                        {s.approverType === 'Role' && (
                          <TextField size="small" select label="Role" value={s.approverRole ?? ''} onChange={(e) => set(i, { approverRole: e.target.value })} sx={{ width: 200 }}>
                            {roles.map((r) => <MenuItem key={r} value={r}>{r}</MenuItem>)}
                          </TextField>
                        )}
                        {s.approverType === 'SpecificUser' && <UserPicker value={s.approverUserId} onChange={(id) => set(i, { approverUserId: id })} />}
                        <IconButton aria-label="Remove step" onClick={() => setDraft({ ...draft, steps: draft.steps.filter((_, n) => n !== i) })}><DeleteOutlineIcon /></IconButton>
                      </Stack>
                      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }}>
                        <TextField size="small" select label="If several approvers" value={s.mode} onChange={(e) => set(i, { mode: e.target.value as WorkflowStep['mode'] })} sx={{ width: 190 }}>
                          <MenuItem value="AnyOne">Any one of them</MenuItem><MenuItem value="All">All of them</MenuItem>
                        </TextField>
                        <TextField size="small" type="number" label="Only from amount" value={s.minAmount ?? ''} onChange={(e) => set(i, { minAmount: num(e.target.value) })} sx={{ width: 150 }} />
                        <TextField size="small" type="number" label="Up to amount" value={s.maxAmount ?? ''} onChange={(e) => set(i, { maxAmount: num(e.target.value) })} sx={{ width: 150 }} />
                        <TextField size="small" type="number" label="Deadline (hours)" value={s.deadlineHours ?? ''} onChange={(e) => set(i, { deadlineHours: num(e.target.value) })} sx={{ width: 140 }} />
                        <TextField size="small" type="number" label="Remind every (h)" value={s.reminderEveryHours ?? ''} onChange={(e) => set(i, { reminderEveryHours: num(e.target.value) })} sx={{ width: 140 }} />
                        <TextField size="small" select label="Escalate to" value={s.escalateToRole ?? ''} onChange={(e) => set(i, { escalateToRole: e.target.value || null })} sx={{ width: 180 }}>
                          <MenuItem value="">Nobody</MenuItem>{roles.map((r) => <MenuItem key={r} value={r}>{r}</MenuItem>)}
                        </TextField>
                      </Stack>
                      <FormControlLabel control={<Checkbox size="small" checked={s.skipIfApproverIsRequester} onChange={(e) => set(i, { skipIfApproverIsRequester: e.target.checked })} />}
                        label="Skip this step when the approver is the requester and a later step exists" />
                    </Stack>
                  </Paper>
                ))}
                <Button sx={{ alignSelf: 'flex-start' }} onClick={() => setDraft({ ...draft, steps: [...draft.steps, newStep(Math.max(0, ...draft.steps.map((s) => s.sequence)) + 1)] })}>Add step</Button>
              </>
            )}
          </Stack>
        </DialogContent>
      )}
      <DialogActions><Button onClick={close}>Cancel</Button><Button variant="contained" disabled={save.isPending || !draft?.name} onClick={() => save.mutate()}>Save as new version</Button></DialogActions>
    </Dialog>
  )
}

export function WorkflowsPage() {
  const qc = useQueryClient()
  const routes = useQuery({ queryKey: ['admin-workflows'], queryFn: workflowAdminApi.list })
  const roles = useQuery({ queryKey: ['admin-workflow-roles'], queryFn: workflowAdminApi.roles })
  const [editing, setEditing] = useState<WorkflowRoute | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  return (
    <Stack spacing={3}>
      <Typography variant="h1">Approval routes</Typography>
      <Card><CardContent>
        {routes.isLoading ? <LoadingState /> : routes.isError ? <ErrorState error={routes.error} onRetry={() => routes.refetch()} /> : routes.data!.length === 0 ? <EmptyState title="No routes" /> : (
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>Request type</TableCell><TableCell>Steps</TableCell><TableCell>Version</TableCell><TableCell align="right">In progress</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>{routes.data!.map((r) => (
              <TableRow key={r.documentType} hover>
                <TableCell>{documentTypeLabel(r.documentType)}<Typography variant="caption" display="block" color="text.secondary">{r.name}</Typography></TableCell>
                <TableCell>
                  {r.owner === 'Nav' ? <Chip size="small" label="Approved in NAV" /> : r.steps.length === 0 ? <Typography variant="body2" color="warning.main">No steps: approved automatically</Typography> : (
                    <Stack divider={<Divider flexItem />} spacing={0.5}>
                      {[...r.steps].sort((a, b) => a.sequence - b.sequence).map((s, i) => <Typography key={i} variant="body2"><strong>{s.sequence}. {s.name || approverLabel[s.approverType]}</strong>: {describe(s)}</Typography>)}
                    </Stack>
                  )}
                </TableCell>
                <TableCell>{r.isDefault ? <Chip size="small" variant="outlined" label="Default" /> : `v${r.version}`}</TableCell>
                <TableCell align="right">{r.runningCount}</TableCell>
                <TableCell align="right"><Button size="small" onClick={() => setEditing(r)}>Edit</Button></TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></TableContainer>
        )}
      </CardContent></Card>
      <RouteEditor route={editing} roles={roles.data ?? []} onClose={() => setEditing(null)} onDone={(m) => { setToast(m); qc.invalidateQueries({ queryKey: ['admin-workflows'] }) }} />
      <Snackbar open={!!toast} autoHideDuration={8000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
