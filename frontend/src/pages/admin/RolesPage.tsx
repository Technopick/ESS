import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, Box, Button, Card, CardActions, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Grid2 as Grid,
  Snackbar, Stack, TextField, Typography,
} from '@mui/material'
import { adminApi, type RoleView } from '../../api/types'
import { errorMessage } from '../../api/client'
import { ErrorState, LoadingState } from '../../components/StateViews'

function RoleDialog({ role, open, onClose, onDone }: { role: RoleView | null; open: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const perms = useQuery({ queryKey: ['permissions'], queryFn: adminApi.permissions })
  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
  const [selected, setSelected] = useState<Set<string>>(new Set(role?.permissions ?? []))
  const [error, setError] = useState<string | null>(null)
  const byModule = useMemo(() => {
    const m = new Map<string, string[]>()
    for (const p of perms.data ?? []) m.set(p.module, [...(m.get(p.module) ?? []), p.code])
    return [...m.entries()]
  }, [perms.data])
  const save = useMutation({
    mutationFn: () => role ? adminApi.updateRole(role.id, name, description || null, [...selected]) : adminApi.createRole(name, description || null, [...selected]),
    onSuccess: () => { onDone(role ? 'Role updated.' : 'Role created.'); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{role ? `Edit ${role.name}` : 'New role'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} disabled={role?.isSystem} helperText={role?.isSystem ? 'System role names cannot change.' : ' '} />
          <TextField label="Description" value={description} onChange={(e) => setDescription(e.target.value)} />
          <Grid container spacing={2}>
            {byModule.map(([module, codes]) => (
              <Grid key={module} size={{ xs: 12, sm: 6, md: 4 }}>
                <Typography variant="subtitle2" sx={{ textTransform: 'capitalize' }}>{module}</Typography>
                {codes.map((c) => (
                  <FormControlLabel key={c} sx={{ display: 'flex' }} label={<Typography variant="body2">{c}</Typography>} control={
                    <Checkbox size="small" checked={selected.has(c)} onChange={(e) => {
                      const n = new Set(selected)
                      if (e.target.checked) n.add(c)
                      else n.delete(c)
                      setSelected(n)
                    }} />
                  } />
                ))}
              </Grid>
            ))}
          </Grid>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending || name.trim().length < 2}>Save</Button>
      </DialogActions>
    </Dialog>
  )
}

export function RolesPage() {
  const qc = useQueryClient()
  const roles = useQuery({ queryKey: ['roles'], queryFn: adminApi.roles })
  const [editing, setEditing] = useState<{ role: RoleView | null } | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const del = useMutation({
    mutationFn: adminApi.deleteRole,
    onSuccess: () => { setToast('Role deleted.'); qc.invalidateQueries({ queryKey: ['roles'] }) },
    onError: (e) => setToast(errorMessage(e)),
  })
  if (roles.isLoading) return <LoadingState />
  if (roles.isError) return <ErrorState error={roles.error} onRetry={() => roles.refetch()} />
  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between">
        <Typography variant="h1">Roles and permissions</Typography>
        <Button variant="contained" onClick={() => setEditing({ role: null })}>New role</Button>
      </Stack>
      <Grid container spacing={2}>
        {roles.data!.map((r) => (
          <Grid key={r.id} size={{ xs: 12, md: 6, lg: 4 }}>
            <Card sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
              <CardContent sx={{ flex: 1 }}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Typography variant="h6">{r.name}</Typography>
                  {r.isSystem && <Chip size="small" label="System" />}
                </Stack>
                <Typography variant="body2" color="text.secondary" gutterBottom>{r.userCount} users · {r.permissions.length} permissions</Typography>
                <Box className="flex flex-wrap gap-1">{r.permissions.slice(0, 8).map((p) => <Chip key={p} size="small" variant="outlined" label={p} />)}{r.permissions.length > 8 && <Chip size="small" label={`+${r.permissions.length - 8}`} />}</Box>
              </CardContent>
              <CardActions>
                <Button size="small" onClick={() => setEditing({ role: r })}>Edit</Button>
                {!r.isSystem && <Button size="small" color="error" disabled={r.userCount > 0} onClick={() => del.mutate(r.id)}>Delete</Button>}
              </CardActions>
            </Card>
          </Grid>
        ))}
      </Grid>
      {editing && (
        <RoleDialog key={editing.role?.id ?? 'new'} role={editing.role} open onClose={() => setEditing(null)}
          onDone={(m) => { setToast(m); qc.invalidateQueries({ queryKey: ['roles'] }) }} />
      )}
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
