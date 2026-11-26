import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, MenuItem, Pagination, Snackbar, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Tooltip, Typography,
} from '@mui/material'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline'
import AttachFileIcon from '@mui/icons-material/AttachFile'
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline'
import { documentsApi, expensesApi, money, type ExpenseClaim, type ExpenseClaimInput, type ExpenseLineInput } from '../api/modules'
import { errorMessage } from '../api/client'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { Statuses } from '../components/Status'
import { ApprovalDialog } from '../components/ApprovalDialog'

const newLine = (): ExpenseLineInput => ({ categoryId: '', expenseDate: dayjs().format('YYYY-MM-DD'), description: '', amount: 0, currency: 'KES', exchangeRate: null, receiptDocumentId: null })

function ClaimEditor({ claimId, open, onClose, onDone }: { claimId: string | null; open: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const cats = useQuery({ queryKey: ['expense-categories'], queryFn: expensesApi.categories })
  const existing = useQuery({ queryKey: ['expense', claimId], queryFn: () => expensesApi.get(claimId!), enabled: open && !!claimId })
  const [form, setForm] = useState<ExpenseClaimInput>({ purpose: '', lines: [newLine()] })
  const [error, setError] = useState<string | null>(null)
  const [warnings, setWarnings] = useState<string[]>([])
  const [uploading, setUploading] = useState<number | null>(null)
  useEffect(() => {
    if (!open) return
    setError(null)
    setWarnings([])
    if (!claimId) setForm({ purpose: '', lines: [newLine()] })
    else if (existing.data) setForm({ purpose: existing.data.purpose, projectCode: existing.data.projectCode, costCentreCode: existing.data.costCentreCode, lines: existing.data.lines.map((l) => ({ ...l })) })
  }, [open, claimId, existing.data])

  const setLine = (i: number, patch: Partial<ExpenseLineInput>) => setForm({ ...form, lines: form.lines.map((l, n) => (n === i ? { ...l, ...patch } : l)) })
  const total = form.lines.reduce((s, l) => s + (Number(l.amount) || 0) * (l.currency && l.currency !== 'KES' ? Number(l.exchangeRate) || 0 : 1), 0)

  const save = useMutation({
    mutationFn: async (submit: boolean) => {
      const body = { ...form, lines: form.lines.map((l) => ({ ...l, amount: Number(l.amount), exchangeRate: l.exchangeRate ? Number(l.exchangeRate) : null })) }
      const claim: ExpenseClaim = claimId ? await expensesApi.update(claimId, body) : await expensesApi.create(body)
      setWarnings(claim.warnings)
      return submit ? expensesApi.submit(claim.id) : claim
    },
    onSuccess: (c, submit) => { onDone(submit ? `${c.reference} sent for approval.` : `${c.reference} saved as a draft.`); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })

  const upload = async (i: number, file: File) => {
    setUploading(i)
    setError(null)
    try { setLine(i, { receiptDocumentId: (await documentsApi.uploadReceipt(file)).id }) } catch (e) { setError(errorMessage(e)) } finally { setUploading(null) }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg">
      <DialogTitle>{claimId ? `Edit ${existing.data?.reference ?? 'claim'}` : 'New expense claim'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          {warnings.map((w) => <Alert key={w} severity="warning">{w}</Alert>)}
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
            <TextField fullWidth label="Purpose" value={form.purpose} onChange={(e) => setForm({ ...form, purpose: e.target.value })} inputProps={{ maxLength: 250 }} />
            <TextField label="Project (optional)" value={form.projectCode ?? ''} onChange={(e) => setForm({ ...form, projectCode: e.target.value })} />
            <TextField label="Cost centre (optional)" value={form.costCentreCode ?? ''} onChange={(e) => setForm({ ...form, costCentreCode: e.target.value })} />
          </Stack>
          <TableContainer>
            <Table size="small">
              <TableHead><TableRow><TableCell>Date</TableCell><TableCell>Category</TableCell><TableCell>Description</TableCell><TableCell>Currency</TableCell><TableCell>Amount</TableCell><TableCell>Receipt</TableCell><TableCell /></TableRow></TableHead>
              <TableBody>
                {form.lines.map((l, i) => {
                  const cat = cats.data?.find((c) => c.id === l.categoryId)
                  return (
                    <TableRow key={i}>
                      <TableCell sx={{ minWidth: 150 }}><TextField size="small" type="date" value={l.expenseDate} onChange={(e) => setLine(i, { expenseDate: e.target.value })} inputProps={{ max: dayjs().format('YYYY-MM-DD'), 'aria-label': 'Date' }} /></TableCell>
                      <TableCell sx={{ minWidth: 170 }}>
                        <TextField size="small" select fullWidth value={l.categoryId} onChange={(e) => setLine(i, { categoryId: e.target.value })} inputProps={{ 'aria-label': 'Category' }}>
                          {(cats.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
                        </TextField>
                      </TableCell>
                      <TableCell sx={{ minWidth: 200 }}><TextField size="small" fullWidth value={l.description} onChange={(e) => setLine(i, { description: e.target.value })} inputProps={{ 'aria-label': 'Description' }} /></TableCell>
                      <TableCell sx={{ width: 150 }}>
                        <Stack direction="row" spacing={0.5}>
                          <TextField size="small" value={l.currency ?? 'KES'} onChange={(e) => setLine(i, { currency: e.target.value.toUpperCase().slice(0, 3) })} sx={{ width: 70 }} inputProps={{ 'aria-label': 'Currency' }} />
                          {l.currency && l.currency !== 'KES' && <TextField size="small" label="Rate" value={l.exchangeRate ?? ''} onChange={(e) => setLine(i, { exchangeRate: e.target.value as unknown as number })} sx={{ width: 80 }} />}
                        </Stack>
                      </TableCell>
                      <TableCell sx={{ width: 130 }}><TextField size="small" type="number" value={l.amount || ''} onChange={(e) => setLine(i, { amount: e.target.value as unknown as number })} inputProps={{ min: 0, step: '0.01', 'aria-label': 'Amount' }} /></TableCell>
                      <TableCell>
                        {l.receiptDocumentId ? (
                          <Tooltip title="View receipt"><IconButton onClick={() => documentsApi.open(l.receiptDocumentId!, true)} color="success"><CheckCircleOutlineIcon /></IconButton></Tooltip>
                        ) : (
                          <Button size="small" component="label" startIcon={<AttachFileIcon />} disabled={uploading === i} color={cat?.requiresReceipt ? 'warning' : 'inherit'}>
                            {uploading === i ? 'Uploading…' : cat?.requiresReceipt ? 'Required' : 'Attach'}
                            <input hidden type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(e) => e.target.files?.[0] && upload(i, e.target.files[0])} />
                          </Button>
                        )}
                      </TableCell>
                      <TableCell>
                        <IconButton aria-label="Remove line" disabled={form.lines.length === 1} onClick={() => setForm({ ...form, lines: form.lines.filter((_, n) => n !== i) })}><DeleteOutlineIcon /></IconButton>
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </TableContainer>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Button onClick={() => setForm({ ...form, lines: [...form.lines, newLine()] })}>Add expense</Button>
            <Typography fontWeight={700}>Total {money(total)}</Typography>
          </Stack>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button onClick={() => save.mutate(false)} disabled={save.isPending}>Save draft</Button>
        <Button variant="contained" onClick={() => save.mutate(true)} disabled={save.isPending}>Submit for approval</Button>
      </DialogActions>
    </Dialog>
  )
}

export function ExpensesPage() {
  const qc = useQueryClient()
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<{ id: string | null } | null>(null)
  const [timeline, setTimeline] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['expenses-mine', page], queryFn: () => expensesApi.mine(page) })
  const refresh = (m: string) => { setToast(m); qc.invalidateQueries({ queryKey: ['expenses-mine'] }) }
  const withdraw = useMutation({ mutationFn: expensesApi.withdraw, onSuccess: () => refresh('Claim withdrawn.'), onError: (e) => setToast(errorMessage(e)) })
  const open = async (id: string) => { const c = await expensesApi.get(id); if (c.workflowInstanceId) setTimeline(c.workflowInstanceId) }

  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">Expense claims</Typography>
        <Button variant="contained" onClick={() => setEditing({ id: null })}>New claim</Button>
      </Stack>
      <Card><CardContent>
        {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} onRetry={() => q.refetch()} /> : q.data!.items.length === 0 ? <EmptyState title="No expense claims yet" /> : (
          <>
            <TableContainer>
              <Table size="small">
                <TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Purpose</TableCell><TableCell align="right">Total</TableCell><TableCell>Status</TableCell><TableCell /></TableRow></TableHead>
                <TableBody>
                  {q.data!.items.map((c) => (
                    <TableRow key={c.id} hover>
                      <TableCell>{c.reference}<Typography variant="caption" display="block" color="text.secondary">{dayjs(c.submittedAtUtc ?? c.createdAtUtc).format('D MMM YYYY')}</Typography></TableCell>
                      <TableCell>{c.purpose}</TableCell>
                      <TableCell align="right">{money(c.totalAmount, c.currency)}</TableCell>
                      <TableCell><Statuses status={c.status} navStatus={c.navStatus} /></TableCell>
                      <TableCell align="right">
                        <Box className="flex justify-end gap-1">
                          {['Draft', 'ReturnedForCorrection'].includes(c.status) && <Button size="small" onClick={() => setEditing({ id: c.id })}>Edit</Button>}
                          {!['Draft'].includes(c.status) && <Button size="small" onClick={() => open(c.id)}>History</Button>}
                          {['Draft', 'ReturnedForCorrection', 'InApproval'].includes(c.status) && c.navStatus === 'NotRequired' && (
                            <Button size="small" color="error" onClick={() => withdraw.mutate(c.id)}>Withdraw</Button>
                          )}
                        </Box>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            <Pagination sx={{ mt: 2 }} count={Math.max(1, Math.ceil(q.data!.totalCount / q.data!.pageSize))} page={page} onChange={(_, p) => setPage(p)} />
          </>
        )}
      </CardContent></Card>
      <ClaimEditor claimId={editing?.id ?? null} open={!!editing} onClose={() => setEditing(null)} onDone={refresh} />
      <ApprovalDialog instanceId={timeline} onClose={() => setTimeline(null)} />
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
