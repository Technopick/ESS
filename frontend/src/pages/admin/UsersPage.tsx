import { useEffect, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, InputAdornment,
  Menu, MenuItem, Pagination, Snackbar, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import SearchIcon from '@mui/icons-material/Search'
import { adminApi, essApi, type RoleAssignmentInput, type UserView } from '../../api/types'
import { errorMessage } from '../../api/client'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'

function RolePicker({ value, onChange }: { value: RoleAssignmentInput[]; onChange: (v: RoleAssignmentInput[]) => void }) {
  const roles = useQuery({ queryKey: ['roles'], queryFn: adminApi.roles })
  const selected = new Set(value.map((r) => r.roleId))
  return (
    <Box className="grid grid-cols-1 sm:grid-cols-2">
      {(roles.data ?? []).map((r) => (
        <FormControlLabel key={r.id} label={r.name} control={
          <Checkbox checked={selected.has(r.id)} onChange={(e) => onChange(e.target.checked ? [...value, { roleId: r.id }] : value.filter((v) => v.roleId !== r.id))} />
        } />
      ))}
    </Box>
  )
}

function CreateUserDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const companies = useQuery({ queryKey: ['companies'], queryFn: essApi.companies })
  const [form, setForm] = useState({ userName: '', email: '', companyId: '', employeeNo: '', sendInvite: true })
  const [roles, setRoles] = useState<RoleAssignmentInput[]>([])
  const [error, setError] = useState<string | null>(null)
  const create = useMutation({
    mutationFn: () => adminApi.createUser({ ...form, companyId: form.companyId || null, employeeNo: form.employeeNo || null, roles }),
    onSuccess: (u) => { onDone(`Account ${u.userName} created${form.sendInvite ? ' and invitation emailed' : ''}.`); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>New user</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="User name" value={form.userName} onChange={(e) => setForm({ ...form, userName: e.target.value })} helperText="Usually the work email" />
          <TextField label="Email" type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
          <TextField select label="Company" value={form.companyId} onChange={(e) => setForm({ ...form, companyId: e.target.value })}>
            <MenuItem value="">None</MenuItem>
            {(companies.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
          </TextField>
          <TextField label="NAV employee no. (optional)" value={form.employeeNo} onChange={(e) => setForm({ ...form, employeeNo: e.target.value })} disabled={!form.companyId} />
          <Typography variant="subtitle2">Roles</Typography>
          <RolePicker value={roles} onChange={setRoles} />
          <FormControlLabel control={<Checkbox checked={form.sendInvite} onChange={(e) => setForm({ ...form, sendInvite: e.target.checked })} />}
            label="Email an invitation link to set a password" />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => create.mutate()} disabled={create.isPending || !form.userName || !form.email}>Create</Button>
      </DialogActions>
    </Dialog>
  )
}

function RolesDialog({ user, onClose, onDone }: { user: UserView | null; onClose: () => void; onDone: (m: string) => void }) {
  const [roles, setRoles] = useState<RoleAssignmentInput[]>([])
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    setRoles(user?.roles.map((r) => ({ roleId: r.roleId, companyId: r.companyId, scopeDepartmentId: r.scopeDepartmentId })) ?? [])
    setError(null)
  }, [user])
  const save = useMutation({
    mutationFn: () => adminApi.setRoles(user!.id, roles),
    onSuccess: () => { onDone('Roles updated. They apply from the user\'s next sign-in or token refresh (within 15 minutes).'); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={!!user} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Roles for {user?.userName}</DialogTitle>
      <DialogContent>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <RolePicker value={roles} onChange={setRoles} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>Save</Button>
      </DialogActions>
    </Dialog>
  )
}

function LinkDialog({ user, onClose, onDone }: { user: UserView | null; onClose: () => void; onDone: (m: string) => void }) {
  const companies = useQuery({ queryKey: ['companies'], queryFn: essApi.companies })
  const [companyId, setCompanyId] = useState('')
  const [no, setNo] = useState('')
  const [error, setError] = useState<string | null>(null)
  const link = useMutation({
    mutationFn: () => adminApi.linkEmployee(user!.id, companyId, no.trim()),
    onSuccess: () => { onDone(`Linked to employee ${no}.`); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={!!user} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Link {user?.userName} to a NAV employee</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
            {(companies.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
          </TextField>
          <TextField label="NAV employee no." value={no} onChange={(e) => setNo(e.target.value)} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => link.mutate()} disabled={link.isPending || !companyId || !no.trim()}>Link</Button>
      </DialogActions>
    </Dialog>
  )
}

function ProvisionDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const companies = useQuery({ queryKey: ['companies'], queryFn: essApi.companies })
  const [companyId, setCompanyId] = useState('')
  const [sendInvites, setSendInvites] = useState(true)
  const [preview, setPreview] = useState<Awaited<ReturnType<typeof adminApi.provision>> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const run = useMutation({
    mutationFn: (dryRun: boolean) => adminApi.provision(companyId, sendInvites, dryRun),
    onSuccess: (r, dryRun) => {
      if (dryRun) setPreview(r)
      else { onDone(`${r.created} accounts created, ${r.linkedExisting} existing accounts linked.`); setPreview(null); onClose() }
    },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Create accounts for NAV employees</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            Creates an Employee account for every active employee with a work email in NAV who doesn't have one yet. Assign manager and HR roles afterwards.
          </Typography>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField select label="Company" value={companyId} onChange={(e) => { setCompanyId(e.target.value); setPreview(null) }}>
            {(companies.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
          </TextField>
          <FormControlLabel control={<Checkbox checked={sendInvites} onChange={(e) => setSendInvites(e.target.checked)} />} label="Email invitation links" />
          {preview && (
            <Alert severity="info">
              {preview.eligible} employees without an account: {preview.created} will get a new account, {preview.linkedExisting} will be linked to an existing one,
              {' '}{preview.skippedNoEmail} have no work email in NAV, {preview.skippedConflict} have conflicts.
              {preview.conflicts.length > 0 && <Box component="ul" sx={{ m: 0, pl: 2 }}>{preview.conflicts.slice(0, 10).map((c) => <li key={c}>{c}</li>)}</Box>}
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button onClick={() => run.mutate(true)} disabled={!companyId || run.isPending}>Preview</Button>
        <Button variant="contained" onClick={() => run.mutate(false)} disabled={!preview || run.isPending || preview.created + preview.linkedExisting === 0}>Create accounts</Button>
      </DialogActions>
    </Dialog>
  )
}

export function UsersPage() {
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [page, setPage] = useState(1)
  const [toast, setToast] = useState<string | null>(null)
  const [menu, setMenu] = useState<{ el: HTMLElement; user: UserView } | null>(null)
  const [rolesFor, setRolesFor] = useState<UserView | null>(null)
  const [linkFor, setLinkFor] = useState<UserView | null>(null)
  const [creating, setCreating] = useState(false)
  const [provisioning, setProvisioning] = useState(false)
  useEffect(() => {
    const t = setTimeout(() => { setDebounced(search); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [search])

  const q = useQuery({ queryKey: ['users', debounced, page], queryFn: () => adminApi.users({ search: debounced || undefined, page }), placeholderData: keepPreviousData })
  const done = (m: string) => { setToast(m); qc.invalidateQueries({ queryKey: ['users'] }) }
  const act = useMutation({
    mutationFn: ({ id, action }: { id: string; action: Parameters<typeof adminApi.action>[1] }) => adminApi.action(id, action),
    onSuccess: (_, v) => done({ disable: 'Account disabled and signed out everywhere.', enable: 'Account enabled.', unlock: 'Account unlocked.',
      'reset-mfa': 'MFA reset. The user must set it up again.', 'send-password-link': 'Password link emailed.' }[v.action]),
    onError: (e) => setToast(errorMessage(e)),
  })
  const unlink = useMutation({
    mutationFn: ({ id, companyId }: { id: string; companyId: string }) => adminApi.unlinkEmployee(id, companyId),
    onSuccess: () => done('Employee link removed.'),
    onError: (e) => setToast(errorMessage(e)),
  })

  return (
    <Stack spacing={3}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" spacing={2}>
        <Typography variant="h1">Users</Typography>
        <Stack direction="row" spacing={1}>
          <Button variant="outlined" onClick={() => setProvisioning(true)}>Create accounts from NAV</Button>
          <Button variant="contained" onClick={() => setCreating(true)}>New user</Button>
        </Stack>
      </Stack>
      <TextField placeholder="Search by user name, email, employee name or number" value={search} onChange={(e) => setSearch(e.target.value)}
        InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> }} inputProps={{ 'aria-label': 'Search users' }} />
      <Card>
        <CardContent>
          {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} onRetry={() => q.refetch()} /> : q.data!.items.length === 0 ? <EmptyState title="No users found" /> : (
            <>
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow><TableCell>User</TableCell><TableCell>Employee</TableCell><TableCell>Roles</TableCell><TableCell>Status</TableCell><TableCell>Last sign-in</TableCell><TableCell /></TableRow>
                  </TableHead>
                  <TableBody>
                    {q.data!.items.map((u) => (
                      <TableRow key={u.id} hover>
                        <TableCell><Typography fontWeight={600}>{u.userName}</Typography><Typography variant="caption" color="text.secondary">{u.email}</Typography></TableCell>
                        <TableCell>{u.employees.map((e) => `${e.employeeNo} ${e.employeeName}`).join(', ') || '–'}</TableCell>
                        <TableCell><Stack direction="row" gap={0.5} flexWrap="wrap">{u.roles.map((r) => <Chip key={r.roleId + (r.scopeDepartmentId ?? '')} size="small" label={r.scopeDepartment ? `${r.role} (${r.scopeDepartment})` : r.role} />)}</Stack></TableCell>
                        <TableCell>
                          <Stack direction="row" gap={0.5} flexWrap="wrap">
                            <Chip size="small" color={u.isActive ? 'success' : 'default'} label={u.isActive ? 'Active' : 'Disabled'} />
                            {u.isLocked && <Chip size="small" color="error" label="Locked" />}
                            {!u.hasPassword && <Chip size="small" color="warning" label="Invited" />}
                            {u.mfaEnabled && <Chip size="small" label="MFA" />}
                          </Stack>
                        </TableCell>
                        <TableCell>{u.lastLoginAtUtc ? dayjs(u.lastLoginAtUtc).format('D MMM YYYY HH:mm') : 'Never'}</TableCell>
                        <TableCell><IconButton aria-label={`Actions for ${u.userName}`} onClick={(e) => setMenu({ el: e.currentTarget, user: u })}><MoreVertIcon /></IconButton></TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mt: 2 }}>
                <Typography variant="body2" color="text.secondary">{q.data!.totalCount} users</Typography>
                <Pagination count={Math.max(1, Math.ceil(q.data!.totalCount / q.data!.pageSize))} page={page} onChange={(_, p) => setPage(p)} />
              </Stack>
            </>
          )}
        </CardContent>
      </Card>

      <Menu anchorEl={menu?.el} open={!!menu} onClose={() => setMenu(null)}>
        {menu && [
          <MenuItem key="roles" onClick={() => { setRolesFor(menu.user); setMenu(null) }}>Edit roles</MenuItem>,
          <MenuItem key="link" onClick={() => { setLinkFor(menu.user); setMenu(null) }}>Link to NAV employee</MenuItem>,
          ...menu.user.employees.map((e) => (
            <MenuItem key={'u' + e.companyId} onClick={() => { unlink.mutate({ id: menu.user.id, companyId: e.companyId }); setMenu(null) }}>Unlink {e.employeeNo}</MenuItem>
          )),
          <MenuItem key="pw" onClick={() => { act.mutate({ id: menu.user.id, action: 'send-password-link' }); setMenu(null) }}>
            {menu.user.hasPassword ? 'Email password reset link' : 'Resend invitation'}
          </MenuItem>,
          menu.user.isLocked && <MenuItem key="unlock" onClick={() => { act.mutate({ id: menu.user.id, action: 'unlock' }); setMenu(null) }}>Unlock</MenuItem>,
          menu.user.mfaEnabled && <MenuItem key="mfa" onClick={() => { act.mutate({ id: menu.user.id, action: 'reset-mfa' }); setMenu(null) }}>Reset MFA</MenuItem>,
          menu.user.isActive
            ? <MenuItem key="dis" sx={{ color: 'error.main' }} onClick={() => { act.mutate({ id: menu.user.id, action: 'disable' }); setMenu(null) }}>Disable account</MenuItem>
            : <MenuItem key="en" onClick={() => { act.mutate({ id: menu.user.id, action: 'enable' }); setMenu(null) }}>Enable account</MenuItem>,
        ]}
      </Menu>
      <CreateUserDialog open={creating} onClose={() => setCreating(false)} onDone={done} />
      <ProvisionDialog open={provisioning} onClose={() => setProvisioning(false)} onDone={done} />
      <RolesDialog user={rolesFor} onClose={() => setRolesFor(null)} onDone={done} />
      <LinkDialog user={linkFor} onClose={() => setLinkFor(null)} onDone={done} />
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
