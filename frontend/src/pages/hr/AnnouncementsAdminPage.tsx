import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Snackbar, Stack, TextField, Typography,
} from '@mui/material'
import { announcementsApi, type AnnouncementAdmin, type AnnouncementInput } from '../../api/admin'
import { errorMessage } from '../../api/client'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { DepartmentSelect, LocationSelect } from '../../components/OrgFilters'

const stateColor = { Live: 'success', Scheduled: 'info', Expired: 'default' } as const
const toLocal = (utc: string | null) => (utc ? dayjs(utc).format('YYYY-MM-DDTHH:mm') : '')
const toUtc = (local: string) => (local ? dayjs(local).toISOString() : null)

/** Turns stored HTML back into editable text when it is simple paragraphs; otherwise keeps the HTML. */
function editableBody(html: string) {
  const simple = /^(<p>[^<]*(<br\s*\/?>[^<]*)*<\/p>\s*)+$/i.test(html.trim())
  if (!simple) return html
  return html.trim().split(/<\/p>\s*/i).filter(Boolean).map((p) => p.replace(/^<p>/i, '').replace(/<br\s*\/?>/gi, '\n')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&')).join('\n\n')
}

function Editor({ item, onClose, onDone }: { item: AnnouncementAdmin | 'new'; onClose: () => void; onDone: (m: string) => void }) {
  const existing = item === 'new' ? null : item
  const [f, setF] = useState({
    title: existing?.title ?? '', body: existing ? editableBody(existing.bodyHtml) : '', publishAt: toLocal(existing?.publishAtUtc ?? null), expiresAt: toLocal(existing?.expiresAtUtc ?? null),
    isPinned: existing?.isPinned ?? false, departmentId: existing?.targetDepartmentId ?? '', locationId: existing?.targetLocationId ?? '',
    notifyOnPublish: existing?.notifyOnPublish ?? true, notifyByEmail: existing?.notifyByEmail ?? false,
  })
  const [error, setError] = useState<string | null>(null)
  const save = useMutation({
    mutationFn: () => {
      const b: AnnouncementInput = { title: f.title, body: f.body, publishAtUtc: toUtc(f.publishAt), expiresAtUtc: toUtc(f.expiresAt), isPinned: f.isPinned,
        targetDepartmentId: f.departmentId || null, targetLocationId: f.locationId || null, notifyOnPublish: f.notifyOnPublish, notifyByEmail: f.notifyOnPublish && f.notifyByEmail }
      return existing ? announcementsApi.update(existing.id, b) : announcementsApi.create(b).then(() => undefined)
    },
    onSuccess: () => { onDone(existing ? 'Announcement updated.' : 'Announcement saved.'); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const alreadyNotified = !!existing?.notifiedAtUtc
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{existing ? 'Edit announcement' : 'New announcement'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <TextField label="Title" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} inputProps={{ maxLength: 200 }} required />
          <TextField label="Message" value={f.body} onChange={(e) => setF({ ...f, body: e.target.value })} multiline minRows={6}
            helperText="Plain text, with a blank line between paragraphs. Basic HTML (bold, lists, https links) is also accepted; anything else is removed." />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="datetime-local" label="Publish at" InputLabelProps={{ shrink: true }} value={f.publishAt} onChange={(e) => setF({ ...f, publishAt: e.target.value })} helperText="Empty to publish now" />
            <TextField fullWidth type="datetime-local" label="Remove at" InputLabelProps={{ shrink: true }} value={f.expiresAt} onChange={(e) => setF({ ...f, expiresAt: e.target.value })} helperText="Empty to keep it" />
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <DepartmentSelect value={f.departmentId} allLabel="Every department" label="Show to department" onChange={(v) => setF({ ...f, departmentId: v })} />
            <LocationSelect value={f.locationId} allLabel="Every location" label="Show to location" onChange={(v) => setF({ ...f, locationId: v })} />
          </Stack>
          <FormControlLabel control={<Checkbox checked={f.isPinned} onChange={(e) => setF({ ...f, isPinned: e.target.checked })} />} label="Pin to the top of the feed" />
          <FormControlLabel disabled={alreadyNotified} control={<Checkbox checked={f.notifyOnPublish} onChange={(e) => setF({ ...f, notifyOnPublish: e.target.checked })} />}
            label={alreadyNotified ? `Staff were notified ${dayjs(existing!.notifiedAtUtc).format('D MMM YYYY, HH:mm')}` : 'Notify the staff it is shown to when it is published'} />
          {f.notifyOnPublish && !alreadyNotified && <FormControlLabel sx={{ pl: 4 }} control={<Checkbox checked={f.notifyByEmail} onChange={(e) => setF({ ...f, notifyByEmail: e.target.checked })} />} label="Also send it by email" />}
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" disabled={!f.title.trim() || !f.body.trim() || save.isPending} onClick={() => save.mutate()}>Save</Button></DialogActions>
    </Dialog>
  )
}

export function AnnouncementsAdminPage() {
  const qc = useQueryClient()
  const [editing, setEditing] = useState<AnnouncementAdmin | 'new' | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['announcements-manage'], queryFn: announcementsApi.manage })
  const refresh = (m: string) => { setToast(m); qc.invalidateQueries({ queryKey: ['announcements-manage'] }); qc.invalidateQueries({ queryKey: ['announcements'] }) }
  const remove = useMutation({ mutationFn: announcementsApi.remove, onSuccess: () => refresh('Announcement deleted.'), onError: (e) => setToast(errorMessage(e)) })
  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">Announcements</Typography>
        <Button variant="contained" onClick={() => setEditing('new')}>New announcement</Button>
      </Stack>
      {list.isLoading ? <LoadingState rows={4} /> : list.isError ? <ErrorState error={list.error} /> : list.data!.length === 0 ? <EmptyState title="No announcements yet" /> : list.data!.map((a) => (
        <Card key={a.id}><CardContent>
          <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" spacing={1}>
            <Box>
              <Stack direction="row" spacing={1} alignItems="center"><Typography variant="h3">{a.title}</Typography><Chip size="small" color={stateColor[a.state]} label={a.state} />{a.isPinned && <Chip size="small" variant="outlined" label="Pinned" />}</Stack>
              <Typography variant="body2" color="text.secondary">
                {a.state === 'Scheduled' ? 'Publishes' : 'Published'} {dayjs(a.publishAtUtc).format('D MMM YYYY, HH:mm')}{a.expiresAtUtc ? ` · removed ${dayjs(a.expiresAtUtc).format('D MMM YYYY, HH:mm')}` : ''}
                {' · '}{[a.targetDepartment, a.targetLocation].filter(Boolean).join(', ') || 'Everyone'}
                {a.notifiedAtUtc ? ' · staff notified' : a.notifyOnPublish ? ' · staff will be notified' : ''}
              </Typography>
            </Box>
            <Stack direction="row" spacing={1} alignItems="flex-start">
              <Button size="small" onClick={() => setEditing(a)}>Edit</Button>
              <Button size="small" color="error" onClick={() => window.confirm(`Delete "${a.title}"?`) && remove.mutate(a.id)}>Delete</Button>
            </Stack>
          </Stack>
          {/* Body HTML is sanitised on the server before it is stored. */}
          <Box sx={{ mt: 1, '& p': { my: 1 }, '& a': { color: 'primary.main' } }} dangerouslySetInnerHTML={{ __html: a.bodyHtml }} />
        </CardContent></Card>
      ))}
      {editing && <Editor item={editing} onClose={() => setEditing(null)} onDone={refresh} />}
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
