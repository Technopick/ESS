import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Stack, Table, TableBody, TableCell, TableRow, TextField, Typography,
} from '@mui/material'
import { approvalsApi, documentTypeLabel, documentsApi } from '../api/modules'
import { errorMessage } from '../api/client'
import { ErrorState, LoadingState } from './StateViews'
import { StatusChip } from './Status'

const actionLabel: Record<string, string> = {
  Submitted: 'Submitted', Approved: 'Approved', Rejected: 'Rejected', Returned: 'Returned for correction', Delegated: 'Delegated',
  Escalated: 'Escalated', Cancelled: 'Cancelled', Resubmitted: 'Resubmitted', AutoApproved: 'Automatic', NavStatusMirrored: 'NAV status',
}

/** Renders a detail value, turning "[receipt:id]" markers into links that open the receipt. */
function DetailValue({ value }: { value: string }) {
  const parts = value.split(/(\[receipt:[0-9a-f-]{36}\])/i)
  return (
    <>
      {parts.map((p, i) => {
        const m = /^\[receipt:([0-9a-f-]{36})\]$/i.exec(p)
        return m ? <Button key={i} size="small" onClick={() => documentsApi.open(m[1], true)}>View receipt</Button> : <span key={i}>{p}</span>
      })}
    </>
  )
}

/** A request's details and approval history; when the viewer has a pending task, also the decision buttons. */
export function ApprovalDialog({ instanceId, onClose }: { instanceId: string | null; onClose: () => void }) {
  const qc = useQueryClient()
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['instance', instanceId], queryFn: () => approvalsApi.instance(instanceId!), enabled: !!instanceId })
  const decide = useMutation({
    mutationFn: (d: 'approve' | 'reject' | 'return') => approvalsApi.decide(q.data!.myTaskId!, d, comment || undefined),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inbox'] })
      qc.invalidateQueries({ queryKey: ['instance', instanceId] })
      setComment('')
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })
  const i = q.data
  return (
    <Dialog open={!!instanceId} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{i ? `${documentTypeLabel(i.documentType)} ${i.reference}` : 'Request'}</DialogTitle>
      <DialogContent dividers>
        {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} /> : i && (
          <Stack spacing={2}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Typography fontWeight={600}>{i.requester}</Typography>
              <StatusChip status={i.status === 'Running' ? 'InApproval' : i.status === 'Returned' ? 'ReturnedForCorrection' : i.status} />
            </Stack>
            <Table size="small">
              <TableBody>
                {i.details.map((d, n) => (
                  <TableRow key={n}>
                    <TableCell sx={{ width: '38%', color: 'text.secondary', verticalAlign: 'top' }}>{d.label}</TableCell>
                    <TableCell><DetailValue value={d.value ?? ''} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            {i.pendingWith.length > 0 && (
              <Alert severity="info">
                Waiting for {i.pendingWith.map((p) => `${p.approver} (${p.step}${p.dueAtUtc ? `, due ${dayjs(p.dueAtUtc).format('D MMM HH:mm')}` : ''})`).join(', ')}
              </Alert>
            )}
            <Divider />
            <Typography variant="subtitle2">History</Typography>
            <Box component="ol" sx={{ m: 0, pl: 2 }}>
              {i.timeline.map((t, n) => (
                <li key={n}>
                  <Typography variant="body2">
                    <strong>{actionLabel[t.action] ?? t.action}</strong> by {t.actor}{t.onBehalfOf ? ` for ${t.onBehalfOf}` : ''} · {dayjs(t.occurredAtUtc).format('D MMM YYYY HH:mm')}
                  </Typography>
                  {t.comment && <Typography variant="body2" color="text.secondary">“{t.comment}”</Typography>}
                </li>
              ))}
            </Box>
            {i.myTaskId && (
              <>
                {error && <Alert severity="error">{error}</Alert>}
                <TextField label="Comment" value={comment} onChange={(e) => setComment(e.target.value)} multiline minRows={2}
                  helperText="Required when you reject or return the request." />
              </>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
        {i?.myTaskId && (
          <>
            <Button color="warning" disabled={decide.isPending} onClick={() => decide.mutate('return')}>Return</Button>
            <Button color="error" disabled={decide.isPending} onClick={() => decide.mutate('reject')}>Reject</Button>
            <Button variant="contained" disabled={decide.isPending} onClick={() => decide.mutate('approve')}>Approve</Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  )
}
