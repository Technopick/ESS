import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Snackbar, Stack,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import {
  authNames, navSettingsApi, platformNames, type NavAuthMethod, type NavCompanyLink, type NavEndpoint, type NavInstance, type NavInstanceInput, type NavPlatform,
} from '../../api/navSettings'
import { errorMessage } from '../../api/client'
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews'
import { NavSetupWizard } from './NavSetupWizard'

const allowedAuth: Record<NavPlatform, NavAuthMethod[]> = {
  Nav2013: ['Windows', 'NavUserPassword'],
  Nav2015: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'], Nav2016: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'],
  Nav2017: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'], Nav2018: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'],
  BusinessCentralOnPrem: ['Windows', 'NavUserPassword', 'WebServiceAccessKey', 'OAuthClientCredentials'], BusinessCentralOnline: ['OAuthClientCredentials'],
}
const isBc = (p: NavPlatform) => p.startsWith('BusinessCentral')

function blank(): NavInstanceInput {
  return { name: '', platform: 'Nav2016', serverHost: '', serviceInstance: 'DynamicsNAV90', soapBaseUrl: '', oDataBaseUrl: '', apiBaseUrl: '', tenantId: '', environmentName: '',
    authMethod: 'NavUserPassword', userName: '', domain: '', password: null, clientId: '', clientSecret: null, timeoutSeconds: 60, validateServerCertificate: true, maxConcurrency: 4, isEnabled: true }
}

function InstanceDialog({ instance, onClose, onDone }: { instance: NavInstance | 'new'; onClose: () => void; onDone: (m: string) => void }) {
  const existing = instance === 'new' ? null : instance
  const [f, setF] = useState<NavInstanceInput>(existing ? { ...existing, password: null, clientSecret: null } : blank())
  const [https, setHttps] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const set = (p: Partial<NavInstanceInput>) => setF({ ...f, ...p })
  const suggest = useMutation({
    mutationFn: () => navSettingsApi.suggestUrls(f.platform, f.serverHost ?? '', f.serviceInstance ?? '', https),
    onSuccess: (u) => set({ soapBaseUrl: u.soapBaseUrl, oDataBaseUrl: u.oDataBaseUrl, serviceInstance: u.serviceInstance }),
    onError: (e) => setError(errorMessage(e)),
  })
  const save = useMutation({
    mutationFn: () => (existing ? navSettingsApi.update(existing.id, f) : navSettingsApi.create(f)),
    onSuccess: () => { onDone(existing ? 'NAV connection saved.' : 'NAV connection added. Add the endpoint mappings next.'); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  const needsSecret = f.authMethod === 'NavUserPassword' || f.authMethod === 'WebServiceAccessKey'
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{existing ? `Edit ${existing.name}` : 'Add a NAV connection'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth label="Name" value={f.name} onChange={(e) => set({ name: e.target.value })} helperText="For example PRODUCTION or TEST" />
            <TextField fullWidth select label="Version" value={f.platform}
              onChange={(e) => { const p = e.target.value as NavPlatform; set({ platform: p, authMethod: allowedAuth[p].includes(f.authMethod) ? f.authMethod : allowedAuth[p][0] }) }}>
              {(Object.keys(platformNames) as NavPlatform[]).map((p) => <MenuItem key={p} value={p}>{platformNames[p]}</MenuItem>)}
            </TextField>
          </Stack>
          {!isBc(f.platform) && <>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems="center">
              <TextField fullWidth label="NAV server" value={f.serverHost ?? ''} onChange={(e) => set({ serverHost: e.target.value })} helperText="Server name as the portal server reaches it" />
              <TextField fullWidth label="Server instance" value={f.serviceInstance ?? ''} onChange={(e) => set({ serviceInstance: e.target.value })} helperText="DynamicsNAV90 for a default NAV 2016 install" />
              <FormControlLabel control={<Checkbox checked={https} onChange={(e) => setHttps(e.target.checked)} />} label="HTTPS" />
              <Button sx={{ whiteSpace: 'nowrap', flexShrink: 0 }} onClick={() => suggest.mutate()} disabled={!f.serverHost}>Fill in addresses</Button>
            </Stack>
          </>}
          <TextField label="SOAP address" value={f.soapBaseUrl ?? ''} onChange={(e) => set({ soapBaseUrl: e.target.value })} helperText="Ends in /WS, e.g. http://navsrv01:7047/DynamicsNAV90/WS" />
          <TextField label="OData address" value={f.oDataBaseUrl ?? ''} onChange={(e) => set({ oDataBaseUrl: e.target.value })} helperText="Ends in /ODataV4 for NAV 2015 and later" />
          {isBc(f.platform) && <TextField label="API address" value={f.apiBaseUrl ?? ''} onChange={(e) => set({ apiBaseUrl: e.target.value })} />}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth select label="Sign-in" value={f.authMethod} onChange={(e) => set({ authMethod: e.target.value as NavAuthMethod })}>
              {allowedAuth[f.platform].map((a) => <MenuItem key={a} value={a}>{authNames[a]}</MenuItem>)}
            </TextField>
            {f.authMethod !== 'OAuthClientCredentials' && <TextField fullWidth label="Service user" value={f.userName ?? ''} onChange={(e) => set({ userName: e.target.value })}
              helperText={f.authMethod === 'Windows' ? 'Empty uses the IIS application pool identity' : 'A dedicated NAV user with the ESS-PORTAL permission set'} />}
            {f.authMethod === 'Windows' && <TextField fullWidth label="Domain" value={f.domain ?? ''} onChange={(e) => set({ domain: e.target.value })} />}
          </Stack>
          {(needsSecret || (f.authMethod === 'Windows' && !!f.userName)) && (
            <TextField type="password" autoComplete="new-password" label={f.authMethod === 'WebServiceAccessKey' ? 'Web service access key' : 'Password'}
              value={f.password ?? ''} onChange={(e) => set({ password: e.target.value })}
              helperText={existing?.hasPassword ? 'Stored encrypted. Leave empty to keep it.' : 'Stored encrypted and never shown again.'} />
          )}
          {f.authMethod === 'OAuthClientCredentials' && <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth label="Tenant ID" value={f.tenantId ?? ''} onChange={(e) => set({ tenantId: e.target.value })} />
            <TextField fullWidth label="Client ID" value={f.clientId ?? ''} onChange={(e) => set({ clientId: e.target.value })} />
            <TextField fullWidth type="password" autoComplete="new-password" label="Client secret" value={f.clientSecret ?? ''} onChange={(e) => set({ clientSecret: e.target.value })}
              helperText={existing?.hasClientSecret ? 'Leave empty to keep it' : undefined} />
          </Stack>}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="number" label="Timeout (seconds)" value={f.timeoutSeconds} onChange={(e) => set({ timeoutSeconds: Number(e.target.value) })} />
            <TextField fullWidth type="number" label="Parallel calls" value={f.maxConcurrency} onChange={(e) => set({ maxConcurrency: Number(e.target.value) })} />
          </Stack>
          <FormControlLabel control={<Checkbox checked={f.validateServerCertificate} onChange={(e) => set({ validateServerCertificate: e.target.checked })} />} label="Check the NAV server's TLS certificate" />
          {!f.validateServerCertificate && <Alert severity="warning">Only switch this off for an internal NAV server with a self-signed certificate. Install a trusted certificate before go-live.</Alert>}
          <FormControlLabel control={<Checkbox checked={f.isEnabled} onChange={(e) => set({ isEnabled: e.target.checked })} />} label="Use this connection" />
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending || !f.name.trim()}>Save</Button></DialogActions>
    </Dialog>
  )
}

function EndpointDialog({ instanceId, endpoint, onClose, onDone }: { instanceId: string; endpoint: NavEndpoint; onClose: () => void; onDone: (m: string) => void }) {
  const [f, setF] = useState(endpoint)
  const [error, setError] = useState<string | null>(null)
  const save = useMutation({
    mutationFn: () => navSettingsApi.saveEndpoint(instanceId, f.logicalName, { protocol: f.protocol, serviceName: f.serviceName, method: f.method, modifiedField: f.modifiedField, fieldMapJson: f.fieldMapJson, isEnabled: f.isEnabled }),
    onSuccess: () => { onDone(`${f.logicalName} saved.`); onClose() },
    onError: (e) => setError(errorMessage(e)),
  })
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{f.logicalName}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error" sx={{ whiteSpace: 'pre-line' }}>{error}</Alert>}
          <TextField select label="Protocol" value={f.protocol} onChange={(e) => setF({ ...f, protocol: e.target.value as NavEndpoint['protocol'] })}>
            <MenuItem value="OData">OData (read)</MenuItem><MenuItem value="Soap">SOAP codeunit</MenuItem><MenuItem value="BcApi">Business Central API</MenuItem>
          </TextField>
          <TextField label="Published service name" value={f.serviceName} onChange={(e) => setF({ ...f, serviceName: e.target.value })} helperText="As entered in the NAV Web Services page" />
          {f.protocol === 'Soap' && <TextField label="Codeunit method" value={f.method ?? ''} onChange={(e) => setF({ ...f, method: e.target.value })} />}
          {f.protocol !== 'Soap' && <TextField label="Last-modified field" value={f.modifiedField ?? ''} onChange={(e) => setF({ ...f, modifiedField: e.target.value })} helperText="For incremental sync, e.g. Last_Date_Modified" />}
          {f.protocol !== 'Soap' && <TextField label="Field map (JSON)" value={f.fieldMapJson ?? ''} onChange={(e) => setF({ ...f, fieldMapJson: e.target.value })} multiline minRows={3}
            helperText='Only fields that differ from the defaults, e.g. {"ContractEndDate": "Contract_Expiry_Date"}' />}
          <FormControlLabel control={<Checkbox checked={f.isEnabled} onChange={(e) => setF({ ...f, isEnabled: e.target.checked })} />} label="Enabled" />
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Cancel</Button><Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending || !f.serviceName.trim()}>Save</Button></DialogActions>
    </Dialog>
  )
}

function Endpoints({ instance, onToast }: { instance: NavInstance; onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState<NavEndpoint | null>(null)
  const data = useQuery({ queryKey: ['nav-endpoints', instance.id], queryFn: () => navSettingsApi.endpoints(instance.id) })
  const refresh = (m: string) => { onToast(m); qc.invalidateQueries({ queryKey: ['nav-endpoints', instance.id] }); qc.invalidateQueries({ queryKey: ['nav-settings'] }) }
  const preset = useMutation({ mutationFn: () => navSettingsApi.applyPreset(instance.id), onSuccess: (r) => refresh(`${r.added} mapping${r.added === 1 ? '' : 's'} added.`), onError: (e) => onToast(errorMessage(e)) })
  if (data.isLoading) return <LoadingState rows={3} />
  if (data.isError) return <ErrorState error={data.error} />
  const d = data.data!
  return (
    <Stack spacing={1}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="subtitle1">Endpoint mappings</Typography>
        {!instance.useSimulator && d.missing.length > 0 && <Button size="small" variant="outlined" onClick={() => preset.mutate()} disabled={preset.isPending}>Add the {d.missing.length} standard ESS mappings</Button>}
      </Stack>
      {d.endpoints.length === 0 ? <Typography variant="body2" color="text.secondary">No mappings yet. Add the standard ones once the ESS objects are imported and published in NAV.</Typography> : (
        <TableContainer><Table size="small">
          <TableHead><TableRow><TableCell>Used for</TableCell><TableCell>Protocol</TableCell><TableCell>NAV service</TableCell><TableCell>Field map</TableCell><TableCell /></TableRow></TableHead>
          <TableBody>{d.endpoints.map((e) => (
            <TableRow key={e.id} hover sx={{ opacity: e.isEnabled ? 1 : 0.5 }}>
              <TableCell>{e.logicalName}</TableCell><TableCell>{e.protocol}</TableCell>
              <TableCell>{e.serviceName}{e.method ? ` · ${e.method}` : ''}</TableCell>
              <TableCell>{e.fieldMapJson ? 'Custom' : 'Default'}</TableCell>
              <TableCell align="right">{!instance.useSimulator && <Button size="small" onClick={() => setEditing(e)}>Edit</Button>}</TableCell>
            </TableRow>
          ))}</TableBody>
        </Table></TableContainer>
      )}
      {editing && <EndpointDialog instanceId={instance.id} endpoint={editing} onClose={() => setEditing(null)} onDone={refresh} />}
    </Stack>
  )
}

function CompanyRow({ company, instances, onToast }: { company: NavCompanyLink; instances: NavInstance[]; onToast: (m: string) => void }) {
  const qc = useQueryClient()
  const [f, setF] = useState({ integrationSettingsId: company.integrationSettingsId ?? '', navCompanyName: company.navCompanyName })
  const save = useMutation({
    mutationFn: () => navSettingsApi.linkCompany(company.id, { integrationSettingsId: f.integrationSettingsId || null, navCompanyName: f.navCompanyName, navCompanyGuid: company.navCompanyGuid }),
    onSuccess: () => { onToast(`${company.displayName} linked.`); qc.invalidateQueries({ queryKey: ['nav-settings'] }) },
    onError: (e) => onToast(errorMessage(e)),
  })
  const changed = f.integrationSettingsId !== (company.integrationSettingsId ?? '') || f.navCompanyName !== company.navCompanyName
  return (
    <TableRow>
      <TableCell>{company.displayName}<Typography variant="caption" display="block" color="text.secondary">{company.code}</Typography></TableCell>
      <TableCell><TextField select size="small" fullWidth value={f.integrationSettingsId} onChange={(e) => setF({ ...f, integrationSettingsId: e.target.value })}>
        <MenuItem value="">Not connected</MenuItem>{instances.map((i) => <MenuItem key={i.id} value={i.id}>{i.name}</MenuItem>)}
      </TextField></TableCell>
      <TableCell><TextField size="small" fullWidth value={f.navCompanyName} onChange={(e) => setF({ ...f, navCompanyName: e.target.value })} /></TableCell>
      <TableCell align="right"><Button size="small" disabled={!changed || save.isPending} onClick={() => save.mutate()}>Save</Button></TableCell>
    </TableRow>
  )
}

export function NavSettingsPage() {
  const [editing, setEditing] = useState<NavInstance | 'new' | null>(null)
  const [wizard, setWizard] = useState<string | 'new' | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const qc = useQueryClient()
  const data = useQuery({ queryKey: ['nav-settings'], queryFn: navSettingsApi.overview })
  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h1">NAV connection</Typography>
        <Stack direction="row" spacing={1}>
          <Button onClick={() => setEditing('new')}>Add manually</Button>
          <Button variant="contained" onClick={() => setWizard('new')}>Connect NAV or Business Central</Button>
        </Stack>
      </Stack>
      <Typography variant="body2" color="text.secondary">
        The portal talks to NAV only through published web services: OData pages to read and the ESS Portal Gateway codeunit (SOAP) to submit.
        The assistant tests the server and sign-in, lists what NAV publishes and matches it to the portal for you.
      </Typography>
      {data.isLoading ? <LoadingState rows={4} /> : data.isError ? <ErrorState error={data.error} /> : <>
        {data.data!.instances.length === 0 ? <EmptyState title="No NAV connection yet" hint="Click Connect NAV or Business Central to set one up step by step." /> : data.data!.instances.map((i) => (
          <Card key={i.id}><CardContent>
            <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" spacing={1}>
              <Box>
                <Stack direction="row" spacing={1} alignItems="center">
                  <Typography variant="h3">{i.name}</Typography>
                  <Chip size="small" label={platformNames[i.platform]} />
                  {i.useSimulator && <Chip size="small" color="warning" label="Simulator (demo data)" />}
                  {!i.isEnabled && <Chip size="small" label="Off" />}
                  {i.lastHealthCheckOk != null && <Chip size="small" color={i.lastHealthCheckOk ? 'success' : 'error'} label={i.lastHealthCheckOk ? 'Last test passed' : 'Last test failed'} />}
                </Stack>
                <Typography variant="body2" color="text.secondary">{i.soapBaseUrl ?? i.apiBaseUrl ?? 'No address'} · {authNames[i.authMethod]}{i.userName ? ` as ${i.userName}` : ''}{i.hasPassword ? ' · secret stored' : ''}</Typography>
              </Box>
              {!i.useSimulator && <Stack direction="row" spacing={1} alignItems="flex-start">
                <Button size="small" variant="outlined" onClick={() => setWizard(i.id)}>Assistant</Button>
                <Button size="small" onClick={() => setEditing(i)}>Edit</Button>
              </Stack>}
            </Stack>
            <Box sx={{ mt: 2 }}><Endpoints instance={i} onToast={setToast} /></Box>
          </CardContent></Card>
        ))}
        <Card><CardContent>
          <Typography variant="subtitle1" gutterBottom>Companies</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>Enter the NAV company name exactly as NAV shows it, including spaces and punctuation.</Typography>
          <TableContainer><Table size="small">
            <TableHead><TableRow><TableCell>Portal company</TableCell><TableCell>NAV connection</TableCell><TableCell>NAV company name</TableCell><TableCell /></TableRow></TableHead>
            <TableBody>{data.data!.companies.map((c) => <CompanyRow key={c.id + (c.integrationSettingsId ?? '') + c.navCompanyName} company={c} instances={data.data!.instances} onToast={setToast} />)}</TableBody>
          </Table></TableContainer>
        </CardContent></Card>
      </>}
      {wizard && <NavSetupWizard settingsId={wizard === 'new' ? null : wizard} onClose={() => setWizard(null)}
        onDone={(m) => { setToast(m); qc.invalidateQueries({ queryKey: ['nav-settings'] }) }} />}
      {editing && <InstanceDialog instance={editing} onClose={() => setEditing(null)} onDone={(m) => { setToast(m); qc.invalidateQueries({ queryKey: ['nav-settings'] }) }} />}
      <Snackbar open={!!toast} autoHideDuration={6000} onClose={() => setToast(null)} message={toast} />
    </Stack>
  )
}
