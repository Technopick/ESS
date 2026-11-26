import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Alert, Button, Card, CardContent, CardHeader, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Grid2 as Grid, Snackbar, Stack,
  Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import { essApi, type Dict } from '../api/types'
import { errorMessage } from '../api/client'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { Permissions, useAuth } from '../auth/AuthContext'

/** Fields an employee can propose per section. Must match the server's allow-list. */
const sections: Record<string, { title: string; fields: [string, string][]; sensitive?: boolean }> = {
  Contact: { title: 'Contact details', fields: [['mobilePhone', 'Mobile phone'], ['personalEmail', 'Personal email'], ['homePhone', 'Home phone']] },
  Address: { title: 'Residential address', fields: [['address', 'Address'], ['address2', 'Address line 2'], ['city', 'Town / city'], ['county', 'County'], ['postCode', 'Postal code'], ['country', 'Country']] },
  EmergencyContacts: { title: 'Emergency contacts', fields: [['name', 'Name'], ['relationship', 'Relationship'], ['phone', 'Phone'], ['action', 'Add, update or remove']] },
  NextOfKin: { title: 'Next of kin', sensitive: true, fields: [['name', 'Name'], ['relationship', 'Relationship'], ['phone', 'Phone'], ['idNumber', 'ID number'], ['percentage', 'Benefit %'], ['action', 'Add, update or remove']] },
  Dependants: { title: 'Dependants', sensitive: true, fields: [['name', 'Name'], ['relationship', 'Relationship'], ['birthDate', 'Date of birth'], ['action', 'Add, update or remove']] },
  Personal: { title: 'Personal details', sensitive: true, fields: [['maritalStatus', 'Marital status'], ['nationalId', 'National ID'], ['kraPin', 'KRA PIN'], ['nssfNo', 'NSSF no.'], ['shifNo', 'SHIF no.'], ['passportNo', 'Passport no.']] },
  BankDetails: { title: 'Bank details', sensitive: true, fields: [['bankName', 'Bank'], ['bankCode', 'Bank code'], ['branch', 'Branch'], ['branchCode', 'Branch code'], ['accountName', 'Account name'], ['accountNo', 'Account number']] },
  Qualifications: { title: 'Qualifications', fields: [['qualification', 'Qualification'], ['institution', 'Institution'], ['from', 'From'], ['to', 'To'], ['expires', 'Expires'], ['action', 'Add, update or remove']] },
}

const label = (k: string) => k.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase())

function KeyValues({ data }: { data: Dict }) {
  const entries = Object.entries(data).filter(([, v]) => v)
  if (entries.length === 0) return <Typography variant="body2" color="text.secondary">Nothing recorded in NAV.</Typography>
  return (
    <Grid container spacing={1}>
      {entries.map(([k, v]) => (
        <Grid key={k} size={{ xs: 12, sm: 6 }}>
          <Typography variant="caption" color="text.secondary">{label(k)}</Typography>
          <Typography>{v}</Typography>
        </Grid>
      ))}
    </Grid>
  )
}

function Rows({ rows }: { rows: Dict[] }) {
  if (rows.length === 0) return <Typography variant="body2" color="text.secondary">Nothing recorded in NAV.</Typography>
  const cols = Array.from(new Set(rows.flatMap((r) => Object.keys(r))))
  return (
    <Table size="small">
      <TableHead><TableRow>{cols.map((c) => <TableCell key={c}>{label(c)}</TableCell>)}</TableRow></TableHead>
      <TableBody>{rows.map((r, i) => <TableRow key={i}>{cols.map((c) => <TableCell key={c}>{r[c] ?? ''}</TableCell>)}</TableRow>)}</TableBody>
    </Table>
  )
}

export function ProfilePage() {
  const qc = useQueryClient()
  const { can } = useAuth()
  const profile = useQuery({ queryKey: ['profile'], queryFn: essApi.profile })
  const requests = useQuery({ queryKey: ['change-requests'], queryFn: essApi.changeRequests })
  const [editing, setEditing] = useState<string | null>(null)
  const [values, setValues] = useState<Dict>({})
  const [reason, setReason] = useState('')
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const submit = useMutation({
    mutationFn: () => essApi.requestChange(editing!, Object.fromEntries(Object.entries(values).filter(([, v]) => v && v.trim())), reason || undefined),
    onSuccess: (r) => {
      setToast(`Change request ${r.reference} sent to HR. NAV is updated only after HR approves it.`)
      setEditing(null)
      qc.invalidateQueries({ queryKey: ['change-requests'] })
    },
    onError: (e) => setError(errorMessage(e)),
  })

  if (profile.isLoading) return <LoadingState rows={5} />
  if (profile.isError) return <ErrorState error={profile.error} onRetry={() => profile.refetch()} />
  const p = profile.data!
  const s = p.summary
  const nav = p.nav
  const pending = new Set((requests.data ?? []).filter((r) => ['Submitted', 'InApproval'].includes(r.status)).map((r) => r.section))
  const canChange = can(Permissions.ProfileRequestChange)

  const section = (key: string, body: React.ReactNode) => (
    <Card key={key}>
      <CardHeader title={sections[key].title}
        action={canChange && (pending.has(key)
          ? <Chip size="small" color="warning" label="Change pending HR review" />
          : <Button size="small" onClick={() => { setEditing(key); setValues({}); setReason(''); setError(null) }}>Request change</Button>)} />
      <CardContent sx={{ pt: 0 }}>{body}</CardContent>
    </Card>
  )

  return (
    <Stack spacing={3}>
      <Typography variant="h1">My profile</Typography>
      {!p.navAvailable && p.navMessage && <Alert severity="warning">{p.navMessage}</Alert>}

      <Card>
        <CardHeader title="Employment" subheader={`Synchronised from NAV ${dayjs(s.lastSyncedAtUtc).format('D MMM YYYY HH:mm')}`} />
        <CardContent sx={{ pt: 0 }}>
          <KeyValues data={{
            employeeNumber: s.employeeNo, name: s.fullName, jobTitle: s.jobTitle, department: s.department, location: s.location,
            reportsTo: s.managerName, status: s.status, dateOfEmployment: s.employmentDate ? dayjs(s.employmentDate).format('D MMM YYYY') : null,
            contractEnds: s.contractEndDate ? dayjs(s.contractEndDate).format('D MMM YYYY') : null, workEmail: s.workEmail,
          }} />
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
            Employment details are maintained by HR in NAV. Contact HR if anything here is wrong.
          </Typography>
        </CardContent>
      </Card>

      {nav && (
        <>
          {section('Contact', <KeyValues data={{ mobilePhone: (nav.core as Dict).mobilePhone ?? null, email: (nav.core as Dict).companyEmail ?? null }} />)}
          {section('Personal', <KeyValues data={nav.personalDetails} />)}
          {section('Address', <KeyValues data={nav.address} />)}
          {section('EmergencyContacts', <Rows rows={nav.emergencyContacts} />)}
          {section('NextOfKin', <Rows rows={nav.nextOfKin} />)}
          {section('Dependants', <Rows rows={nav.dependants} />)}
          {section('Qualifications', <Rows rows={nav.qualifications} />)}
          {nav.bankDetailsMasked && section('BankDetails', <KeyValues data={nav.bankDetailsMasked} />)}
        </>
      )}

      <Card>
        <CardHeader title="My change requests" />
        <CardContent sx={{ pt: 0 }}>
          {requests.isLoading ? <LoadingState rows={2} /> : !requests.data?.length ? <EmptyState title="No change requests" /> : (
            <Table size="small">
              <TableHead><TableRow><TableCell>Reference</TableCell><TableCell>Section</TableCell><TableCell>Changes</TableCell><TableCell>Status</TableCell><TableCell>Date</TableCell></TableRow></TableHead>
              <TableBody>
                {requests.data.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell>{r.reference}</TableCell>
                    <TableCell>{sections[r.section]?.title ?? r.section}</TableCell>
                    <TableCell>{Object.entries(r.changes ?? {}).map(([k, v]) => `${label(k)}: ${v}`).join('; ')}</TableCell>
                    <TableCell><Chip size="small" label={r.status} /></TableCell>
                    <TableCell>{dayjs(r.createdAtUtc).format('D MMM YYYY')}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <Dialog open={!!editing} onClose={() => setEditing(null)} fullWidth maxWidth="sm">
        <DialogTitle>Request a change: {editing && sections[editing].title}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              Fill in only what should change. HR reviews the request before NAV is updated.
              {editing && sections[editing].sensitive && ' This information is stored encrypted.'}
            </Typography>
            {error && <Alert severity="error">{error}</Alert>}
            {editing && sections[editing].fields.map(([k, l]) => (
              <TextField key={k} label={l} value={values[k] ?? ''} onChange={(e) => setValues({ ...values, [k]: e.target.value })} inputProps={{ maxLength: 250 }} />
            ))}
            <TextField label="Reason (optional)" multiline minRows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setEditing(null)}>Cancel</Button>
          <Button variant="contained" onClick={() => submit.mutate()} disabled={submit.isPending || !Object.values(values).some((v) => v?.trim())}>Send to HR</Button>
        </DialogActions>
      </Dialog>
      <Snackbar open={!!toast} autoHideDuration={8000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
