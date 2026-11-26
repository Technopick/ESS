import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, Menu, MenuItem, Snackbar, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import { documentsApi, type DocumentItem } from '../api/modules'
import { errorMessage } from '../api/client'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'

const size = (b: number) => (b > 1024 * 1024 ? `${(b / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`)

function UploadDialog({ open, versionOf, onClose, onDone }: { open: boolean; versionOf: DocumentItem | null; onClose: () => void; onDone: (m: string) => void }) {
  const cats = useQuery({ queryKey: ['doc-categories'], queryFn: documentsApi.categories })
  const [file, setFile] = useState<File | null>(null)
  const [form, setForm] = useState({ categoryId: '', title: '', expiryDate: '' })
  const [error, setError] = useState<string | null>(null)
  const save = useMutation({
    mutationFn: () => versionOf ? documentsApi.newVersion(versionOf.id, file!, form.expiryDate || undefined) : documentsApi.upload(file!, form),
    onSuccess: () => { onDone(versionOf ? 'New version uploaded.' : 'Document uploaded.'); setFile(null); setForm({ categoryId: '', title: '', expiryDate: '' }); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{versionOf ? `New version of ${versionOf.title}` : 'Upload a document'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          {!versionOf && (
            <>
              <TextField select label="Category" value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })}>
                {(cats.data ?? []).filter((c) => c.employeeCanUpload && c.code !== 'RECEIPT').map((c) => <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>)}
              </TextField>
              <TextField label="Title" value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} inputProps={{ maxLength: 200 }} />
            </>
          )}
          <TextField type="date" label="Expiry date (if any)" InputLabelProps={{ shrink: true }} value={form.expiryDate} onChange={(e) => setForm({ ...form, expiryDate: e.target.value })} />
          <Button variant="outlined" component="label">
            {file ? file.name : 'Choose file'}
            <input hidden type="file" accept=".pdf,.jpg,.jpeg,.png,.docx,.xlsx" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </Button>
          <Typography variant="caption" color="text.secondary">PDF, JPEG, PNG, Word or Excel, up to 10 MB.</Typography>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => save.mutate()} disabled={!file || (!versionOf && !form.categoryId) || save.isPending}>Upload</Button>
      </DialogActions>
    </Dialog>
  )
}

export function DocumentsPage() {
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['documents-mine'], queryFn: documentsApi.mine })
  const [upload, setUpload] = useState<{ versionOf: DocumentItem | null } | null>(null)
  const [menu, setMenu] = useState<{ el: HTMLElement; doc: DocumentItem } | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const refresh = (m: string) => { setToast(m); qc.invalidateQueries({ queryKey: ['documents-mine'] }) }
  const remove = useMutation({ mutationFn: documentsApi.remove, onSuccess: () => refresh('Document deleted.'), onError: (e) => setToast(errorMessage(e)) })
  const open = (id: string, inline: boolean) => documentsApi.open(id, inline).catch((e) => setToast(errorMessage(e)))

  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">Documents</Typography>
        <Button variant="contained" onClick={() => setUpload({ versionOf: null })}>Upload</Button>
      </Stack>
      <Card><CardContent>
        {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} onRetry={() => q.refetch()} /> : q.data!.length === 0 ? <EmptyState title="No documents yet" hint="Your contract, letters from HR and company policies appear here." /> : (
          <TableContainer>
            <Table size="small">
              <TableHead><TableRow><TableCell>Document</TableCell><TableCell>Category</TableCell><TableCell>Expires</TableCell><TableCell>Updated</TableCell><TableCell /></TableRow></TableHead>
              <TableBody>
                {q.data!.map((d) => {
                  const expiring = d.expiryDate && dayjs(d.expiryDate).diff(dayjs(), 'day') <= 30
                  return (
                    <TableRow key={d.id} hover>
                      <TableCell>
                        <Button size="small" onClick={() => open(d.id, true)} sx={{ textAlign: 'left' }}>{d.title}</Button>
                        <Typography variant="caption" color="text.secondary" display="block" sx={{ pl: 1 }}>{d.fileName} · {size(d.sizeBytes)}{d.currentVersion > 1 ? ` · version ${d.currentVersion}` : ''}</Typography>
                      </TableCell>
                      <TableCell>{d.category}{d.visibility === 'Company' && <Chip size="small" sx={{ ml: 1 }} label="Company" />}</TableCell>
                      <TableCell>{d.expiryDate ? <Typography variant="body2" color={expiring ? 'error' : undefined}>{dayjs(d.expiryDate).format('D MMM YYYY')}</Typography> : '–'}</TableCell>
                      <TableCell>{dayjs(d.uploadedAtUtc).format('D MMM YYYY')}</TableCell>
                      <TableCell align="right"><IconButton aria-label={`Actions for ${d.title}`} onClick={(e) => setMenu({ el: e.currentTarget, doc: d })}><MoreVertIcon /></IconButton></TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </CardContent></Card>
      <Menu anchorEl={menu?.el} open={!!menu} onClose={() => setMenu(null)}>
        {menu && [
          <MenuItem key="dl" onClick={() => { open(menu.doc.id, false); setMenu(null) }}>Download</MenuItem>,
          menu.doc.canDelete && <MenuItem key="v" onClick={() => { setUpload({ versionOf: menu.doc }); setMenu(null) }}>Upload new version</MenuItem>,
          menu.doc.canDelete && <MenuItem key="del" sx={{ color: 'error.main' }} onClick={() => { remove.mutate(menu.doc.id); setMenu(null) }}>Delete</MenuItem>,
        ]}
      </Menu>
      <UploadDialog open={!!upload} versionOf={upload?.versionOf ?? null} onClose={() => setUpload(null)} onDone={refresh} />
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
