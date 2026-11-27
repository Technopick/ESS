import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import {
  Alert, Box, Button, Card, CardContent, CardHeader, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Grid2 as Grid,
  Snackbar, Stack, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, Typography,
} from '@mui/material'
import CheckCircleIcon from '@mui/icons-material/CheckCircle'
import ErrorIcon from '@mui/icons-material/Error'
import HelpOutlineIcon from '@mui/icons-material/HelpOutline'
import { integrationApi, type ConnectionTestResult, type QueueStatus } from '../../api/integration'
import { errorMessage } from '../../api/client'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { Permissions, useAuth } from '../../auth/AuthContext'
import { ExportMenu } from '../../components/ExportMenu'
import { download } from '../../api/admin'

dayjs.extend(relativeTime)
const ago = (v: string | null) => (v ? dayjs(v).fromNow() : 'never')

const filters: { label: string; status?: number }[] = [
  { label: 'Needs attention', status: undefined },
  { label: 'Failed', status: 3 },
  { label: 'Dead letter', status: 4 },
  { label: 'Reconciliation', status: 5 },
  { label: 'Pending', status: 0 },
  { label: 'Succeeded', status: 2 },
]

const statusColor: Record<QueueStatus, 'default' | 'success' | 'error' | 'warning' | 'info'> = {
  Pending: 'info', Processing: 'info', Succeeded: 'success', Failed: 'error', DeadLetter: 'error', RequiresReconciliation: 'warning', Cancelled: 'default',
}

function Metric({ label, value, tone }: { label: string; value: string | number; tone?: 'error' | 'warning' }) {
  return (
    <Card>
      <CardContent>
        <Typography variant="body2" color="text.secondary">{label}</Typography>
        <Typography variant="h5" fontWeight={700} color={tone ? `${tone}.main` : undefined}>{value}</Typography>
      </CardContent>
    </Card>
  )
}

export function IntegrationDashboardPage() {
  const qc = useQueryClient()
  const { can } = useAuth()
  const canOperate = can(Permissions.IntegrationOperate)
  const [tab, setTab] = useState(0)
  const [toast, setToast] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<ConnectionTestResult | null>(null)

  const status = useQuery({ queryKey: ['nav-status'], queryFn: integrationApi.status, refetchInterval: 15000 })
  const tx = useQuery({
    queryKey: ['nav-tx', filters[tab].status, tab],
    queryFn: async () => {
      if (tab !== 0) return integrationApi.transactions(filters[tab].status)
      // "Needs attention" = failed + dead letter + reconciliation
      const pages = await Promise.all([3, 4, 5].map((s) => integrationApi.transactions(s)))
      return { items: pages.flatMap((p) => p.items), page: 1, pageSize: 75, totalCount: pages.reduce((a, p) => a + p.totalCount, 0) }
    },
    refetchInterval: 15000,
  })

  const test = useMutation({
    mutationFn: integrationApi.test,
    onSuccess: (r) => { setTestResult(r); qc.invalidateQueries({ queryKey: ['nav-status'] }) },
    onError: (e) => setToast(errorMessage(e)),
  })
  const sync = useMutation({
    mutationFn: ({ companyId, full }: { companyId: string; full: boolean }) => integrationApi.sync(companyId, full),
    onSuccess: () => setToast('Synchronisation queued. Results appear here when it finishes.'),
    onError: (e) => setToast(errorMessage(e)),
  })
  const retry = useMutation({
    mutationFn: integrationApi.retry,
    onSuccess: (r: { status: string; navDocumentNo?: string; lastError?: string }) => {
      setToast(r.status === 'Succeeded' ? `Confirmed by NAV as ${r.navDocumentNo}.` : `Retry finished: ${r.status}. ${r.lastError ?? ''}`)
      qc.invalidateQueries({ queryKey: ['nav-tx'] })
      qc.invalidateQueries({ queryKey: ['nav-status'] })
    },
    onError: (e) => setToast(errorMessage(e)),
  })

  if (status.isLoading) return <LoadingState rows={4} />
  if (status.isError) return <ErrorState error={status.error} onRetry={() => status.refetch()} />
  const s = status.data!

  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">NAV integration</Typography>
        {/* Logs are masked on the server; the export covers the last 7 days. */}
        <ExportMenu onExport={(format) => download('/integration/nav/logs/export', { format })} onError={(e) => setToast(errorMessage(e))} />
      </Stack>

      <Grid container spacing={2}>
        <Grid size={{ xs: 6, md: 2 }}><Metric label="Pending" value={s.pending + s.processing} /></Grid>
        <Grid size={{ xs: 6, md: 2 }}><Metric label="Succeeded (24h)" value={s.succeeded24h} /></Grid>
        <Grid size={{ xs: 6, md: 2 }}><Metric label="Failed" value={s.failed + s.deadLetter} tone={s.failed + s.deadLetter ? 'error' : undefined} /></Grid>
        <Grid size={{ xs: 6, md: 2 }}><Metric label="Reconciliation" value={s.requiresReconciliation} tone={s.requiresReconciliation ? 'warning' : undefined} /></Grid>
        <Grid size={{ xs: 6, md: 2 }}><Metric label="Avg response (24h)" value={s.averageResponseMs24h == null ? '–' : `${Math.round(s.averageResponseMs24h)} ms`} /></Grid>
        <Grid size={{ xs: 6, md: 2 }}><Metric label="Availability (24h)" value={s.availability24h == null ? '–' : `${(s.availability24h * 100).toFixed(1)}%`} /></Grid>
      </Grid>

      <Typography variant="body2" color="text.secondary">
        Last successful sync {ago(s.lastSuccessfulSyncUtc)} · last failed sync {ago(s.lastFailedSyncUtc)}
      </Typography>

      {s.instances.length === 0 && <EmptyState title="No NAV instance configured" hint="Add NAV connection settings to start synchronising." />}
      {s.instances.map((i) => (
        <Card key={i.settingsId}>
          <CardHeader
            avatar={i.lastHealthCheckOk == null ? <HelpOutlineIcon color="disabled" /> : i.lastHealthCheckOk ? <CheckCircleIcon color="success" /> : <ErrorIcon color="error" />}
            title={<Box className="flex items-center gap-2">{i.name}{i.simulator && <Chip size="small" color="warning" label="Simulator – demo data" />}{!i.enabled && <Chip size="small" label="Disabled" />}</Box>}
            subheader={`${i.platform} · ${i.server ?? 'no server'} · ${i.companies.join(', ') || 'no company assigned'}`}
            action={canOperate && (
              <Button onClick={() => test.mutate(i.settingsId)} disabled={test.isPending}>
                {test.isPending && test.variables === i.settingsId ? 'Testing…' : 'Test connection'}
              </Button>
            )}
          />
          <CardContent sx={{ pt: 0 }}>
            <Typography variant="body2" color="text.secondary">
              Last check {ago(i.lastHealthCheckAtUtc)}{i.lastHealthCheckMessage ? `: ${i.lastHealthCheckMessage}` : ''}
            </Typography>
          </CardContent>
        </Card>
      ))}
      {canOperate && <SyncButtons onSync={(companyId, full) => sync.mutate({ companyId, full })} busy={sync.isPending} />}

      <Card>
        <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" sx={{ px: 1 }}>
          {filters.map((f) => <Tab key={f.label} label={f.label} />)}
        </Tabs>
        <CardContent>
          {tx.isLoading ? <LoadingState /> : tx.isError ? <ErrorState error={tx.error} onRetry={() => tx.refetch()} /> :
            tx.data!.items.length === 0 ? <EmptyState title="No transactions here" /> : (
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Operation</TableCell><TableCell>Reference</TableCell><TableCell>Status</TableCell>
                      <TableCell>Attempts</TableCell><TableCell>NAV document</TableCell><TableCell>Last error</TableCell><TableCell>Created</TableCell><TableCell />
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {tx.data!.items.map((t) => (
                      <TableRow key={t.id} hover>
                        <TableCell>{t.operation}</TableCell>
                        <TableCell sx={{ fontFamily: 'monospace', fontSize: 12 }}>{t.idempotencyKey}</TableCell>
                        <TableCell><Chip size="small" color={statusColor[t.status]} label={t.status} /></TableCell>
                        <TableCell>{t.attempts}/{t.maxAttempts}</TableCell>
                        <TableCell>{t.navDocumentNo ?? '–'}</TableCell>
                        <TableCell sx={{ maxWidth: 320 }}>{t.lastError ?? '–'}</TableCell>
                        <TableCell>{ago(t.createdAtUtc)}</TableCell>
                        <TableCell>
                          {canOperate && !['Succeeded', 'Cancelled', 'Processing'].includes(t.status) && (
                            <Button size="small" onClick={() => retry.mutate(t.id)} disabled={retry.isPending}>Retry</Button>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
        </CardContent>
      </Card>

      {s.recentErrors.length > 0 && (
        <Card>
          <CardHeader title="Recent NAV call errors" />
          <CardContent sx={{ pt: 0 }}>
            <Stack spacing={1}>
              {s.recentErrors.map((e, idx) => (
                <Alert key={idx} severity="error" variant="outlined">
                  <strong>{e.operation}</strong> {e.statusCode ? `(HTTP ${e.statusCode})` : ''} {ago(e.occurredAtUtc)}: {e.error}
                </Alert>
              ))}
            </Stack>
          </CardContent>
        </Card>
      )}

      <Dialog open={!!testResult} onClose={() => setTestResult(null)} maxWidth="md" fullWidth>
        <DialogTitle>Connection test</DialogTitle>
        <DialogContent>
          {testResult && (
            <>
              <Alert severity={testResult.success ? 'success' : 'error'} sx={{ mb: 2 }}>{testResult.message} ({testResult.latencyMs} ms)</Alert>
              <Table size="small">
                <TableHead><TableRow><TableCell>Endpoint</TableCell><TableCell>Protocol</TableCell><TableCell>Service</TableCell><TableCell>Result</TableCell></TableRow></TableHead>
                <TableBody>
                  {testResult.endpoints.map((p) => (
                    <TableRow key={p.logicalName}>
                      <TableCell>{p.logicalName}</TableCell><TableCell>{p.protocol}</TableCell><TableCell>{p.serviceName}</TableCell>
                      <TableCell>{p.reachable ? <Chip size="small" color="success" label="OK" /> : <Chip size="small" color="error" label={p.httpStatus ?? 'Error'} title={p.message ?? ''} />}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </>
          )}
        </DialogContent>
        <DialogActions><Button onClick={() => setTestResult(null)}>Close</Button></DialogActions>
      </Dialog>
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}

function SyncButtons({ onSync, busy }: { onSync: (companyId: string, full: boolean) => void; busy: boolean }) {
  const { me } = useAuth()
  if (!me?.companyId) return <Typography variant="body2" color="text.secondary">Set a default company on your account to run a manual sync.</Typography>
  return (
    <Stack direction="row" spacing={1}>
      <Button variant="contained" onClick={() => onSync(me.companyId!, false)} disabled={busy}>Sync changes now</Button>
      <Button variant="outlined" onClick={() => onSync(me.companyId!, true)} disabled={busy}>Full sync</Button>
    </Stack>
  )
}
