import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Box, Button, Card, CardContent, Chip, Divider, FormControlLabel, Grid2 as Grid, List, ListItemButton, ListItemText, MenuItem, Snackbar, Stack, Switch, Tab,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from '@mui/material'
import PrintOutlinedIcon from '@mui/icons-material/PrintOutlined'
import { money, payrollApi, type Payslip, type PayslipLine } from '../api/modules'
import { errorMessage } from '../api/client'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'

const groups: { title: string; match: (l: PayslipLine) => boolean }[] = [
  { title: 'Earnings', match: (l) => ['Earning', 'Allowance', 'Bonus', 'Overtime'].includes(l.group) },
  { title: 'Statutory deductions', match: (l) => l.group === 'Statutory' },
  { title: 'Other deductions', match: (l) => l.group === 'Deduction' },
  { title: 'Reliefs and information', match: (l) => ['Relief', 'Information'].includes(l.group) },
]

function PayslipView({ slip }: { slip: Payslip }) {
  const savePdf = async () => {
    const blob = await payrollApi.pdf(slip.periodCode)
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `payslip-${slip.periodCode}.pdf`
    a.click()
    setTimeout(() => URL.revokeObjectURL(url), 30_000)
  }
  return (
    <Card className="payslip-print">
      <CardContent>
        <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" spacing={2}>
          <div>
            <Typography variant="h5">Payslip · {slip.periodName}</Typography>
            <Typography color="text.secondary">{slip.employeeName} · {slip.employeeNo}{slip.jobTitle ? ` · ${slip.jobTitle}` : ''}{slip.department ? ` · ${slip.department}` : ''}</Typography>
            {slip.paymentDate && <Typography variant="body2" color="text.secondary">Paid on {dayjs(slip.paymentDate).format('D MMMM YYYY')}</Typography>}
          </div>
          <Stack direction="row" spacing={1} className="no-print" alignItems="flex-start">
            {slip.pdfAvailable && <Button variant="outlined" onClick={savePdf}>Download PDF</Button>}
            <Button variant="outlined" startIcon={<PrintOutlinedIcon />} onClick={() => window.print()}>Print</Button>
          </Stack>
        </Stack>
        {slip.fromCache && (
          <Alert severity="warning" sx={{ mt: 2 }} className="no-print">
            NAV could not be reached, so this is the copy retrieved on {dayjs(slip.retrievedAtUtc).format('D MMM YYYY HH:mm')}.
          </Alert>
        )}
        <Grid container spacing={2} sx={{ my: 2 }}>
          {[['Gross pay', slip.grossPay], ['Deductions', slip.totalDeductions], ['Net pay', slip.netPay]].map(([label, value]) => (
            <Grid key={label as string} size={{ xs: 12, sm: 4 }}>
              <Box sx={{ p: 2, borderRadius: 2, bgcolor: 'action.hover' }}>
                <Typography variant="body2" color="text.secondary">{label}</Typography>
                <Typography variant="h5" fontWeight={label === 'Net pay' ? 700 : 500}>{money(value as number, slip.currency)}</Typography>
              </Box>
            </Grid>
          ))}
        </Grid>
        {groups.map((g) => {
          const lines = slip.lines.filter(g.match)
          if (lines.length === 0) return null
          return (
            <Box key={g.title} sx={{ mb: 2 }}>
              <Typography variant="subtitle2" sx={{ mb: 0.5 }}>{g.title}</Typography>
              <Table size="small">
                <TableHead><TableRow><TableCell>Item</TableCell><TableCell align="right">This period</TableCell><TableCell align="right">Year to date</TableCell></TableRow></TableHead>
                <TableBody>
                  {lines.map((l) => (
                    <TableRow key={l.code}>
                      <TableCell>{l.description}</TableCell>
                      <TableCell align="right">{money(l.amount, '')}</TableCell>
                      <TableCell align="right">{l.yearToDate !== null ? money(l.yearToDate, '') : ''}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )
        })}
        <Divider sx={{ my: 1 }} />
        <Typography variant="caption" color="text.secondary">Figures come from the payroll in NAV. Contact payroll if anything looks wrong.</Typography>
      </CardContent>
    </Card>
  )
}

function Statutory() {
  const [year, setYear] = useState(dayjs().year())
  const q = useQuery({ queryKey: ['statutory', year], queryFn: () => payrollApi.statutory(year) })
  return (
    <Card><CardContent>
      <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
        <Typography variant="h6">Statutory deductions</Typography>
        <TextField select size="small" value={year} onChange={(e) => setYear(Number(e.target.value))}>
          {[0, 1, 2].map((n) => <MenuItem key={n} value={dayjs().year() - n}>{dayjs().year() - n}</MenuItem>)}
        </TextField>
      </Stack>
      {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} /> : q.data!.months.length === 0 ? <EmptyState title="No published payslips for this year" /> : (
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Month</TableCell><TableCell align="right">Gross</TableCell><TableCell align="right">Taxable</TableCell>
                {q.data!.codes.map((c) => <TableCell key={c} align="right">{q.data!.labels[c] ?? c}</TableCell>)}
              </TableRow>
            </TableHead>
            <TableBody>
              {q.data!.months.map((m) => (
                <TableRow key={m.periodCode}>
                  <TableCell>{m.periodName}{!m.available && <Chip size="small" sx={{ ml: 1 }} label="Not available" />}</TableCell>
                  <TableCell align="right">{money(m.grossPay, '')}</TableCell>
                  <TableCell align="right">{money(m.taxablePay, '')}</TableCell>
                  {q.data!.codes.map((c) => <TableCell key={c} align="right">{money(m.amounts[c] ?? 0, '')}</TableCell>)}
                </TableRow>
              ))}
              <TableRow sx={{ '& td': { fontWeight: 700 } }}>
                <TableCell>Total</TableCell>
                <TableCell align="right">{money(q.data!.totalGross, '')}</TableCell>
                <TableCell align="right">{money(q.data!.totalTaxable, '')}</TableCell>
                {q.data!.codes.map((c) => <TableCell key={c} align="right">{money(q.data!.totals[c] ?? 0, '')}</TableCell>)}
              </TableRow>
            </TableBody>
          </Table>
        </TableContainer>
      )}
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
        For your records and tax returns. Your official P9 form comes from payroll.
      </Typography>
    </CardContent></Card>
  )
}

export function PayslipsPage() {
  const [tab, setTab] = useState(0)
  const periods = useQuery({ queryKey: ['payroll-periods'], queryFn: payrollApi.periods })
  const [selected, setSelected] = useState<string | null>(null)
  useEffect(() => { if (!selected && periods.data?.length) setSelected(periods.data[0].code) }, [periods.data, selected])
  const slip = useQuery({ queryKey: ['payslip', selected], queryFn: () => payrollApi.payslip(selected!), enabled: !!selected, retry: false })

  return (
    <Stack spacing={3}>
      <Typography variant="h1" className="no-print">Payslips</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} className="no-print">
        <Tab label="Payslips" />
        <Tab label="Tax and statutory summary" />
      </Tabs>
      {tab === 0 && (periods.isLoading ? <LoadingState /> : periods.isError ? <ErrorState error={periods.error} /> : periods.data!.length === 0 ? <EmptyState title="No payslips yet" hint="Payslips appear here once payroll publishes them." /> : (
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, md: 3 }} className="no-print">
            <Card><List dense>
              {periods.data!.map((p) => (
                <ListItemButton key={p.code} selected={p.code === selected} onClick={() => setSelected(p.code)}>
                  <ListItemText primary={p.name} secondary={p.payDate ? `Paid ${dayjs(p.payDate).format('D MMM YYYY')}` : undefined} />
                </ListItemButton>
              ))}
            </List></Card>
          </Grid>
          <Grid size={{ xs: 12, md: 9 }}>
            {slip.isLoading ? <LoadingState rows={6} /> : slip.isError ? <ErrorState error={slip.error} onRetry={() => slip.refetch()} /> : slip.data && <PayslipView slip={slip.data} />}
          </Grid>
        </Grid>
      ))}
      {tab === 1 && <Statutory />}
    </Stack>
  )
}

/** Payroll staff: publish closed NAV payroll periods so employees can see their payslips. */
export function PayrollPublishPage() {
  const qc = useQueryClient()
  const [notify, setNotify] = useState(true)
  const [toast, setToast] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['admin-payroll-periods'], queryFn: payrollApi.adminPeriods })
  const act = useMutation({
    mutationFn: ({ id, publish }: { id: string; publish: boolean }) => publish ? payrollApi.publish(id, notify) : payrollApi.unpublish(id),
    onSuccess: (_, v) => { setToast(v.publish ? 'Published. Employees can now see their payslips.' : 'Unpublished.'); qc.invalidateQueries({ queryKey: ['admin-payroll-periods'] }) },
    onError: (e) => setToast(errorMessage(e)),
  })
  return (
    <Stack spacing={3}>
      <Typography variant="h1">Payroll publication</Typography>
      <Alert severity="info">Close the period in NAV first. Publishing only makes the NAV payslips visible; it does not change payroll.</Alert>
      <FormControlLabel control={<Switch checked={notify} onChange={(e) => setNotify(e.target.checked)} />} label="Notify employees when publishing" />
      <Card><CardContent>
        {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} /> : q.data!.length === 0 ? <EmptyState title="No payroll periods synchronised from NAV" /> : (
          <Table size="small">
            <TableHead><TableRow><TableCell>Period</TableCell><TableCell>Pay date</TableCell><TableCell>NAV</TableCell><TableCell>Portal</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>
              {q.data!.map((p) => (
                <TableRow key={p.id}>
                  <TableCell>{p.name}</TableCell>
                  <TableCell>{p.payDate ? dayjs(p.payDate).format('D MMM YYYY') : '–'}</TableCell>
                  <TableCell><Chip size="small" label={p.closed ? 'Closed' : 'Open'} color={p.closed ? 'success' : 'default'} /></TableCell>
                  <TableCell>{p.published ? <Chip size="small" color="success" label={`Published ${dayjs(p.publishedAtUtc).format('D MMM')}`} /> : 'Not published'}</TableCell>
                  <TableCell align="right">
                    {p.published
                      ? <Button size="small" color="error" onClick={() => act.mutate({ id: p.id, publish: false })}>Unpublish</Button>
                      : <Button size="small" variant="contained" disabled={!p.closed || act.isPending} onClick={() => act.mutate({ id: p.id, publish: true })}>Publish</Button>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent></Card>
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
