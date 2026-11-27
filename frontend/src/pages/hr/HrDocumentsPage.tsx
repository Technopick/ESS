import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem, Pagination, Snackbar, Stack,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Tooltip, Typography,
} from '@mui/material'
import DownloadIcon from '@mui/icons-material/Download'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import { documentsApi } from '../../api/modules'
import { errorMessage } from '../../api/client'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { EmployeePicker } from '../../components/EmployeePicker'
import { DepartmentSelect } from '../../components/OrgFilters'

const visibilityName: Record<string, string> = { EmployeeOnly: 'Employee and HR', HrOnly: 'HR only', Company: 'Everyone', Department: 'Department' }

function UploadDialog({ open, employeeId, employeeName, onClose, onDone }: { open: boolean; employeeId: string | null; employeeName: string | null; onClose: () => void; onDone: (m: string) => void }) {
  const categories = useQuery({ queryKey: ['doc-categories'], queryFn: documentsApi.categories })
  const [target, setTarget] = useState<'employee' | 'Company' | 'Department'>('employee')
  const [form, setForm] = useState({ employeeId: employeeId ?? '', employeeName: employeeName ?? '', categoryId: '', title: '', expiryDate: '', hrOnly: false, departmentId: '' })
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)
  const upload = useMutation({
    mutationFn: () => documentsApi.upload(file!, target === 'employee'
      ? { employeeId: form.employeeId, categoryId: form.categoryId, title: form.title, expiryDate: form.expiryDate, visibility: form.hrOnly ? 'HrOnly' : undefined }
      : { categoryId: form.categoryId, title: form.title, expiryDate: form.expiryDate, visibility: target, departmentId: target === 'Department' ? form.departmentId : undefined }),
    onSuccess: (d) => { onDone(`${d.title} uploaded.`); setFile(null); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const ready = !!file && !!form.categoryId && (target !== 'employee' || !!form.employeeId) && (target !== 'Department' || !!form.departmentId)
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Upload a document</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <ToggleButtonGroup exclusive size="small" value={target} onChange={(_, v) => v && setTarget(v)}>
            <ToggleButton value="employee">For one employee</ToggleButton><ToggleButton value="Company">For everyone</ToggleButton><ToggleButton value="Department">For a department</ToggleButton>
          </ToggleButtonGroup>
          {target === 'employee' && <>
            <EmployeePicker value={form.employeeId || null} valueLabel={form.employeeName} onChange={(id, name) => setForm({ ...form, employeeId: id ?? '', employeeName: name ?? '' })} />
            <FormControlLabel control={<Checkbox checked={form.hrOnly} onChange={(e) => setForm({ ...form, hrOnly: e.target.checked })} />} label="Keep it hidden from the employee (HR only)" />
          </>}
          {target === 'Department' && <DepartmentSelect value={form.departmentId} allLabel="Choose a department" onChange={(v) => setForm({ ...form, departmentId: v })} />}
          <TextField select label="Category" value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })}>
            {(categories.data ?? []).filter((c) => c.code !== 'RECEIPT').map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
          </TextField>
          <TextField label="Title" value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} helperText="Leave empty to use the category name" />
          <TextField type="date" label="Expiry date" InputLabelProps={{ shrink: true }} value={form.expiryDate} onChange={(e) => setForm({ ...form, expiryDate: e.target.value })} helperText="For permits, licences and certificates. HR is reminded before it expires." />
          <Button component="label" variant="outlined" startIcon={<UploadFileIcon />}>{file ? file.name : 'Choose file (PDF, image or Office, up to 10 MB)'}
            <input hidden type="file" accept=".pdf,.png,.jpg,.jpeg,.doc,.docx,.xls,.xlsx" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </Button>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" disabled={!ready || upload.isPending} onClick={() => upload.mutate()}>Upload</Button></DialogActions>
    </Dialog>
  )
}

export function HrDocumentsPage() {
  const qc = useQueryClient()
  const [params, setParams] = useSearchParams()
  const employeeId = params.get('employeeId')
  const [employeeName, setEmployeeName] = useState<string | null>(null)
  const [categoryId, setCategoryId] = useState('')
  const [expiringOnly, setExpiringOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [uploading, setUploading] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const categories = useQuery({ queryKey: ['doc-categories'], queryFn: documentsApi.categories })
  const list = useQuery({ queryKey: ['hr-documents', employeeId, categoryId, expiringOnly, page], queryFn: () => documentsApi.list({ employeeId: employeeId ?? undefined, categoryId, expiringOnly, page }) })
  const remove = useMutation({ mutationFn: documentsApi.remove, onSuccess: () => { setToast('Document deleted.'); qc.invalidateQueries({ queryKey: ['hr-documents'] }) }, onError: (e) => setToast(errorMessage(e)) })
  const today = dayjs()
  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">Employee documents</Typography>
        <Button variant="contained" startIcon={<UploadFileIcon />} onClick={() => setUploading(true)}>Upload</Button>
      </Stack>
      <Stack direction="row" spacing={2} useFlexGap flexWrap="wrap" alignItems="center">
        <EmployeePicker label="Employee (all when empty)" value={employeeId} valueLabel={employeeName}
          onChange={(id, name) => { setEmployeeName(name); setPage(1); setParams(id ? { employeeId: id } : {}) }} />
        <TextField select size="small" label="Category" value={categoryId} onChange={(e) => { setCategoryId(e.target.value); setPage(1) }} sx={{ minWidth: 200 }}>
          <MenuItem value="">All categories</MenuItem>{(categories.data ?? []).map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
        </TextField>
        <FormControlLabel control={<Checkbox checked={expiringOnly} onChange={(e) => { setExpiringOnly(e.target.checked); setPage(1) }} />} label="Expired or expiring within 60 days" />
      </Stack>
      <Card><CardContent>
        {list.isLoading ? <LoadingState rows={6} /> : list.isError ? <ErrorState error={list.error} /> : list.data!.items.length === 0 ? <EmptyState title="No documents found" /> : (
          <>
            <TableContainer><Table size="small">
              <TableHead><TableRow><TableCell>Document</TableCell><TableCell>Employee</TableCell><TableCell>Category</TableCell><TableCell>Who can see it</TableCell><TableCell>Expires</TableCell><TableCell>Uploaded</TableCell><TableCell /></TableRow></TableHead>
              <TableBody>{list.data!.items.map((d) => {
                const expiry = d.expiryDate ? dayjs(d.expiryDate) : null
                return (
                  <TableRow key={d.id} hover>
                    <TableCell>{d.title}<Typography variant="caption" display="block" color="text.secondary">{d.fileName} · version {d.currentVersion}{d.scanned ? '' : ' · not yet scanned'}</Typography></TableCell>
                    <TableCell>{d.employeeName ?? '–'}</TableCell>
                    <TableCell>{d.category}</TableCell>
                    <TableCell>{visibilityName[d.visibility] ?? d.visibility}</TableCell>
                    <TableCell>{expiry ? <Stack direction="row" spacing={1} alignItems="center"><span>{expiry.format('D MMM YYYY')}</span>
                      {expiry.isBefore(today, 'day') ? <Chip size="small" color="error" label="Expired" /> : expiry.diff(today, 'day') <= 60 ? <Chip size="small" color="warning" label="Soon" /> : null}</Stack> : '–'}</TableCell>
                    <TableCell>{dayjs(d.uploadedAtUtc).format('D MMM YYYY')}<Typography variant="caption" display="block" color="text.secondary">{d.uploadedBy}</Typography></TableCell>
                    <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                      <Tooltip title="Download"><IconButton aria-label={`Download ${d.title}`} onClick={() => documentsApi.open(d.id).catch((e) => setToast(errorMessage(e)))}><DownloadIcon /></IconButton></Tooltip>
                      {d.canDelete && <Tooltip title="Delete"><IconButton aria-label={`Delete ${d.title}`} onClick={() => window.confirm(`Delete ${d.title}? Every version is removed.`) && remove.mutate(d.id)}><DeleteOutlineIcon /></IconButton></Tooltip>}
                    </TableCell>
                  </TableRow>
                )
              })}</TableBody>
            </Table></TableContainer>
            <Pagination sx={{ mt: 2 }} count={Math.max(1, Math.ceil(list.data!.totalCount / list.data!.pageSize))} page={page} onChange={(_, p) => setPage(p)} />
          </>
        )}
      </CardContent></Card>
      {uploading && <UploadDialog open employeeId={employeeId} employeeName={employeeName} onClose={() => setUploading(false)} onDone={(m) => { setToast(m); qc.invalidateQueries({ queryKey: ['hr-documents'] }) }} />}
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
