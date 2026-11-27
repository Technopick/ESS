import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardActionArea, CardContent, Chip, Grid2 as Grid, MenuItem, Snackbar, Stack, Table, TableBody, TableCell, TableContainer, TableFooter,
  TableHead, TablePagination, TableRow, TextField, Typography,
} from '@mui/material'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import { reportsApi, statusName, type ColumnType, type ReportInfo, type ReportQuery } from '../api/admin'
import { payrollApi } from '../api/modules'
import { errorMessage } from '../api/client'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { ExportMenu } from '../components/ExportMenu'
import { DepartmentSelect, LocationSelect } from '../components/OrgFilters'

const numeric: ColumnType[] = ['Number', 'Decimal', 'Money', 'Percent']

function cell(v: string | number | boolean | null, type: ColumnType): string {
  if (v === null || v === undefined || v === '') return ''
  if (typeof v === 'boolean') return v ? 'Yes' : 'No'
  switch (type) {
    case 'Money': return Number(v).toLocaleString('en-KE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    case 'Percent': return `${Number(v).toLocaleString('en-KE', { maximumFractionDigits: 1 })}%`
    case 'Number': case 'Decimal': return Number(v).toLocaleString('en-KE', { maximumFractionDigits: 2 })
    case 'Date': return dayjs(String(v)).format('D MMM YYYY')
    case 'DateTime': return dayjs(String(v)).format('D MMM YYYY HH:mm')
    default: return String(v)
  }
}

function ReportView({ report, canExport, onBack }: { report: ReportInfo; canExport: boolean; onBack: () => void }) {
  const f = report.filters
  const [draft, setDraft] = useState<ReportQuery>({})
  const [query, setQuery] = useState<ReportQuery>({})
  const [page, setPage] = useState(0)
  const [rowsPerPage, setRowsPerPage] = useState(50)
  const [toast, setToast] = useState<string | null>(null)
  const periods = useQuery({ queryKey: ['payroll-periods'], queryFn: payrollApi.adminPeriods, enabled: f.period })
  const q = useQuery({ queryKey: ['report', report.code, query], queryFn: () => reportsApi.run(report.code, query) })
  const set = (patch: Partial<ReportQuery>) => setDraft({ ...draft, ...patch })
  const visible = useMemo(() => q.data?.rows.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage) ?? [], [q.data, page, rowsPerPage])

  return (
    <Stack spacing={3}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
        <div>
          <Button startIcon={<ArrowBackIcon />} onClick={onBack} sx={{ mb: 1 }}>All reports</Button>
          <Typography variant="h1">{report.name}</Typography>
          <Typography variant="body2" color="text.secondary">{report.description}</Typography>
        </div>
        {canExport && <Box><ExportMenu onExport={(fmt) => reportsApi.export(report.code, query, fmt)} onError={(e) => setToast(errorMessage(e))} disabled={!q.data} /></Box>}
      </Stack>
      <Stack direction="row" spacing={2} useFlexGap flexWrap="wrap" alignItems="center" component="form" aria-label="Filters"
        onSubmit={(e) => { e.preventDefault(); setPage(0); setQuery(draft) }}>
        {f.dateRange && <TextField size="small" type="date" label="From" InputLabelProps={{ shrink: true }} value={draft.from ?? ''} onChange={(e) => set({ from: e.target.value })} />}
        {f.dateRange && <TextField size="small" type="date" label="To" InputLabelProps={{ shrink: true }} value={draft.to ?? ''} onChange={(e) => set({ to: e.target.value })} />}
        {f.period && (
          <TextField select size="small" label="Payroll period" value={draft.periodCode ?? ''} onChange={(e) => set({ periodCode: e.target.value })} sx={{ minWidth: 200 }}>
            <MenuItem value="">Latest closed period</MenuItem>
            {(periods.data ?? []).filter((p) => p.closed).map((p) => <MenuItem key={p.id} value={p.code}>{p.name}</MenuItem>)}
          </TextField>
        )}
        {f.department && <DepartmentSelect value={draft.departmentId ?? ''} onChange={(v) => set({ departmentId: v })} />}
        {f.location && <LocationSelect value={draft.locationId ?? ''} onChange={(v) => set({ locationId: v })} />}
        {f.statuses && (
          <TextField select size="small" label="Status" value={draft.status ?? ''} onChange={(e) => set({ status: e.target.value })} sx={{ minWidth: 160 }}>
            <MenuItem value="">Any status</MenuItem>
            {f.statuses.map((s) => <MenuItem key={s} value={s}>{statusName(s)}</MenuItem>)}
          </TextField>
        )}
        <Button type="submit" variant="contained">Run report</Button>
      </Stack>
      <Card><CardContent>
        {q.isLoading ? <LoadingState rows={6} /> : q.isError ? <ErrorState error={q.error} onRetry={() => q.refetch()} /> : q.data && (
          <Stack spacing={2}>
            <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
              {q.data.filters.map((x) => <Chip key={x.key} size="small" label={`${x.key}: ${x.value}`} />)}
              <Chip size="small" variant="outlined" label={`${q.data.rows.length.toLocaleString()} rows`} />
            </Stack>
            {q.data.note && <Typography variant="body2" color="text.secondary">{q.data.note}</Typography>}
            {q.data.truncated && <Alert severity="warning">Only the first {q.data.rows.length.toLocaleString()} rows are shown. Narrow the filters to see the rest.</Alert>}
            {q.data.rows.length === 0 ? <EmptyState title="No records match these filters" /> : (
              <>
                <TableContainer sx={{ maxHeight: 640 }}>
                  <Table size="small" stickyHeader>
                    <TableHead><TableRow>{q.data.columns.map((c) => <TableCell key={c.key} align={numeric.includes(c.type) ? 'right' : 'left'} sx={{ whiteSpace: 'nowrap' }}>{c.label}</TableCell>)}</TableRow></TableHead>
                    <TableBody>
                      {visible.map((r, i) => (
                        <TableRow key={i} hover>
                          {q.data!.columns.map((c, j) => <TableCell key={c.key} align={numeric.includes(c.type) ? 'right' : 'left'} sx={{ fontVariantNumeric: 'tabular-nums' }}>{cell(r[j], c.type)}</TableCell>)}
                        </TableRow>
                      ))}
                    </TableBody>
                    {q.data.totals && (
                      <TableFooter><TableRow>
                        {q.data.columns.map((c, j) => <TableCell key={c.key} align={numeric.includes(c.type) ? 'right' : 'left'} sx={{ fontWeight: 700, color: 'text.primary' }}>{cell(q.data!.totals![j], c.type)}</TableCell>)}
                      </TableRow></TableFooter>
                    )}
                  </Table>
                </TableContainer>
                <TablePagination component="div" count={q.data.rows.length} page={page} onPageChange={(_, p) => setPage(p)} rowsPerPage={rowsPerPage}
                  onRowsPerPageChange={(e) => { setRowsPerPage(Number(e.target.value)); setPage(0) }} rowsPerPageOptions={[25, 50, 100, 250]} />
              </>
            )}
          </Stack>
        )}
      </CardContent></Card>
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}

export function ReportsPage() {
  const catalog = useQuery({ queryKey: ['reports-catalog'], queryFn: reportsApi.catalog })
  const [selected, setSelected] = useState<ReportInfo | null>(null)
  if (selected) return <ReportView report={selected} canExport={!!catalog.data?.canExport} onBack={() => setSelected(null)} />
  const groups = Object.entries((catalog.data?.reports ?? []).reduce<Record<string, ReportInfo[]>>((acc, r) => ({ ...acc, [r.category]: [...(acc[r.category] ?? []), r] }), {}))
  return (
    <Stack spacing={3}>
      <div>
        <Typography variant="h1">Reports</Typography>
        <Typography variant="body2" color="text.secondary">Reports show the departments you are responsible for. Every view and export is recorded in the audit log.</Typography>
      </div>
      {catalog.isLoading ? <LoadingState /> : catalog.isError ? <ErrorState error={catalog.error} /> : groups.length === 0 ? <EmptyState title="No reports are available to you" /> : (
        <>
          {!catalog.data!.canExport && <Alert severity="info" variant="outlined">You can view reports. Exporting them needs the data export permission.</Alert>}
          {groups.map(([category, reports]) => (
            <Stack key={category} spacing={1}>
              <Typography variant="overline">{category}</Typography>
              <Grid container spacing={2}>
                {reports.map((r) => (
                  <Grid key={r.code} size={{ xs: 12, sm: 6, lg: 4 }}>
                    <Card sx={{ height: '100%' }}><CardActionArea sx={{ height: '100%' }} onClick={() => setSelected(r)}><CardContent>
                      <Typography variant="subtitle1" fontWeight={600}>{r.name}</Typography>
                      <Typography variant="body2" color="text.secondary">{r.description}</Typography>
                    </CardContent></CardActionArea></Card>
                  </Grid>
                ))}
              </Grid>
            </Stack>
          ))}
        </>
      )}
    </Stack>
  )
}
