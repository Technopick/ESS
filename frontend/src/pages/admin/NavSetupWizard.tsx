import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  Alert, Box, Button, Card, CardActionArea, CardContent, Checkbox, Chip, CircularProgress, Collapse, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, Link, List, ListItem, ListItemIcon, ListItemText, MenuItem, Radio, RadioGroup, Stack, Step, StepLabel, Stepper, Table, TableBody,
  TableCell, TableHead, TableRow, TextField, Typography, useMediaQuery, useTheme,
} from '@mui/material'
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline'
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline'
import { Link as RouterLink } from 'react-router-dom'
import {
  navSetupApi, platformNames, type NavAuthMethod, type NavConnectResult, type NavDiscoverResult, type NavFieldsResult, type NavGatewaySuggestion,
  type NavPlatform, type NavPublishedService, type NavSetupCheck, type NavSetupCompany, type NavSetupConnection, type NavSetupMapping,
} from '../../api/navSettings'
import { errorMessage } from '../../api/client'

type Family = 'nav' | 'bcOnPrem' | 'bcOnline'
const navVersions: NavPlatform[] = ['Nav2013', 'Nav2015', 'Nav2016', 'Nav2017', 'Nav2018']
const defaultInstance: Partial<Record<NavPlatform, string>> = {
  Nav2013: 'DynamicsNAV71', Nav2015: 'DynamicsNAV80', Nav2016: 'DynamicsNAV90', Nav2017: 'DynamicsNAV100', Nav2018: 'DynamicsNAV110', BusinessCentralOnPrem: 'BC',
}
const signIns: Record<NavPlatform, NavAuthMethod[]> = {
  Nav2013: ['Windows', 'NavUserPassword'],
  Nav2015: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'], Nav2016: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'],
  Nav2017: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'], Nav2018: ['Windows', 'NavUserPassword', 'WebServiceAccessKey'],
  BusinessCentralOnPrem: ['Windows', 'NavUserPassword', 'WebServiceAccessKey', 'OAuthClientCredentials'], BusinessCentralOnline: ['OAuthClientCredentials'],
}
const signInText: Record<NavAuthMethod, [string, string]> = {
  Windows: ['Windows', 'NAV signs in Windows accounts (the usual set-up when NAV and the portal are in the same domain).'],
  NavUserPassword: ['NAV user name and password', 'NAV is set to NavUserPassword. Use a NAV user made for the portal, for example ESS-SVC.'],
  WebServiceAccessKey: ['Web service access key', 'A NAV user and the key from their user card, instead of the password.'],
  OAuthClientCredentials: ['Microsoft Entra app', 'An app registered in Entra ID and added in Business Central under Microsoft Entra Applications.'],
}
const familyOf = (p: NavPlatform): Family => (p === 'BusinessCentralOnline' ? 'bcOnline' : p === 'BusinessCentralOnPrem' ? 'bcOnPrem' : 'nav')
const statusChip = { found: { label: 'Found', color: 'success' }, suggested: { label: 'Suggested, check it', color: 'info' }, missing: { label: 'Not published', color: 'default' } } as const

interface Selection { service: NavPublishedService | null; fields: NavFieldsResult | null; fieldMap: Record<string, string | null>; loading: boolean; error: string | null }

function blankConnection(): NavSetupConnection {
  return { platform: 'Nav2016', serverHost: '', serviceInstance: '', https: false, soapPort: null, oDataPort: null, tenantId: '', environmentName: 'Production',
    authMethod: 'Windows', userName: '', domain: '', password: '', clientId: '', clientSecret: '', validateServerCertificate: true }
}

function Checks({ checks }: { checks: NavSetupCheck[] }) {
  return (
    <List dense disablePadding>
      {checks.map((c, i) => (
        <ListItem key={i} disableGutters alignItems="flex-start">
          <ListItemIcon sx={{ minWidth: 32, mt: 0.5 }}>{c.ok ? <CheckCircleOutlineIcon color="success" /> : <ErrorOutlineIcon color="error" />}</ListItemIcon>
          <ListItemText primary={<><b>{c.step}.</b> {c.message}</>} secondary={c.hint} />
        </ListItem>
      ))}
    </List>
  )
}

/**
 * The NAV / Business Central connection assistant: pick the system, enter the server and sign-in, test it, pick the company,
 * then choose which published service serves each portal feature (with suggestions and field matching), and save.
 */
export function NavSetupWizard({ settingsId, onClose, onDone }: { settingsId: string | null; onClose: () => void; onDone: (message: string) => void }) {
  const theme = useTheme()
  const narrow = useMediaQuery(theme.breakpoints.down('sm'))
  const start = useQuery({ queryKey: ['nav-setup-start', settingsId], queryFn: () => navSetupApi.start(settingsId), gcTime: 0 })
  const [step, setStep] = useState(0)
  const [conn, setConn] = useState<NavSetupConnection>(blankConnection)
  const [advanced, setAdvanced] = useState(false)
  const [connected, setConnected] = useState<NavConnectResult | null>(null)
  const [company, setCompany] = useState<NavSetupCompany | null>(null)
  const [portalCompanyId, setPortalCompanyId] = useState('')
  const [discovered, setDiscovered] = useState<NavDiscoverResult | null>(null)
  const [selections, setSelections] = useState<Record<string, Selection>>({})
  const [openFields, setOpenFields] = useState<string | null>(null)
  const [gateway, setGateway] = useState<NavGatewaySuggestion | null>(null)
  const [name, setName] = useState('')
  const [runSync, setRunSync] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const saved = start.data?.saved ?? null
  const resultRef = useRef<HTMLDivElement>(null)
  useEffect(() => { if (connected) resultRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }) }, [connected])

  // Editing: start from the saved connection (never its secrets) and the company linked to it.
  useEffect(() => {
    if (!start.data) return
    const s = start.data.saved
    if (s) {
      setConn({ ...blankConnection(), ...s, password: '', clientSecret: '', serviceInstance: s.serviceInstance ?? '', serverHost: s.serverHost ?? '' })
      setName(s.name)
      if ((s.soapPort && s.soapPort !== 7047) || (s.oDataPort && s.oDataPort !== 7048)) setAdvanced(true)
    }
    const linked = start.data.portalCompanies.find((c) => c.integrationSettingsId === settingsId && settingsId)
    const real = start.data.portalCompanies.filter((c) => c.code !== 'SIM')
    setPortalCompanyId((linked ?? real[0] ?? start.data.portalCompanies[0])?.id ?? '')
  }, [start.data, settingsId])

  const set = (p: Partial<NavSetupConnection>) => { setConn((c) => ({ ...c, ...p })); setConnected(null); setDiscovered(null) }
  const family = familyOf(conn.platform)
  const base = { connection: { ...conn, password: conn.password || null, clientSecret: conn.clientSecret || null }, settingsId }
  const ctxFor = (c: NavSetupCompany) => ({ ...base, company: c.name, companyId: c.id })

  const chooseFamily = (f: Family) => {
    const platform: NavPlatform = f === 'nav' ? (familyOf(conn.platform) === 'nav' ? conn.platform : 'Nav2016') : f === 'bcOnPrem' ? 'BusinessCentralOnPrem' : 'BusinessCentralOnline'
    set({ platform, authMethod: signIns[platform].includes(conn.authMethod) ? conn.authMethod : signIns[platform][0] })
  }

  const connect = useMutation({
    mutationFn: () => navSetupApi.connect(base),
    onMutate: () => setError(null),
    onSuccess: (r) => {
      setConnected(r)
      if (r.ok) {
        const keep = r.companies.find((c) => c.name === start.data?.portalCompanies.find((p) => p.id === portalCompanyId)?.navCompanyName)
        setCompany(keep ?? (r.companies.length === 1 ? r.companies[0] : null))
      }
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const loadFields = async (logicalName: string, service: NavPublishedService | null, c: NavSetupCompany, savedMap?: string | null) => {
    if (!service) { setSelections((s) => ({ ...s, [logicalName]: { service: null, fields: null, fieldMap: {}, loading: false, error: null } })); return }
    setSelections((s) => ({ ...s, [logicalName]: { service, fields: null, fieldMap: {}, loading: true, error: null } }))
    try {
      const r = await navSetupApi.fields({ ...ctxFor(c), logicalName, protocol: service.protocol, serviceName: service.name })
      const map: Record<string, string | null> = {}
      const savedOverrides = savedMap ? (JSON.parse(savedMap) as Record<string, string>) : {}
      for (const f of r.fields) map[f.essField] = savedOverrides[f.essField] ?? f.navField
      setSelections((s) => ({ ...s, [logicalName]: { service, fields: r, fieldMap: map, loading: false, error: r.ok ? null : r.message } }))
    } catch (e) {
      setSelections((s) => ({ ...s, [logicalName]: { service, fields: null, fieldMap: {}, loading: false, error: errorMessage(e) } }))
    }
  }

  const discover = useMutation({
    mutationFn: (c: NavSetupCompany) => navSetupApi.discover(ctxFor(c)),
    onMutate: () => setError(null),
    onSuccess: (r, c) => {
      setDiscovered(r)
      const savedRows = new Map((saved?.mappings ?? []).map((m) => [m.logicalName, m]))
      for (const f of r.features) {
        // Editing: keep what was saved when NAV still publishes it; otherwise take the suggestion.
        const prev = savedRows.get(f.logicalName)
        const prevService = prev?.isEnabled ? r.services.find((s) => s.name === prev.serviceName && s.protocol === prev.protocol) : undefined
        void loadFields(f.logicalName, prevService ?? f.service, c, prevService ? prev?.fieldMapJson : null)
      }
      const prevGateway = (saved?.mappings ?? []).find((m) => m.protocol === 'Soap' && m.isEnabled)?.serviceName
      if (prevGateway && prevGateway !== r.gateway.serviceName && r.gateway.candidates.includes(prevGateway)) pickGateway.mutate({ c, name: prevGateway, list: r.gateway.candidates })
      else setGateway(r.gateway)
      if (!name) setName(family === 'nav' ? platformNames[conn.platform] : family === 'bcOnPrem' ? 'Business Central' : 'Business Central online')
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const pickGateway = useMutation({
    mutationFn: ({ c, name: n, list }: { c: NavSetupCompany; name: string; list: string[] }) =>
      navSetupApi.gateway({ ...ctxFor(c), serviceName: n, codeunits: list }),
    onSuccess: (g) => setGateway(g),
    onError: (e) => setError(errorMessage(e)),
  })

  const mappings = useMemo((): NavSetupMapping[] => {
    if (!discovered) return []
    const rows: NavSetupMapping[] = discovered.features.map((f) => {
      const s = selections[f.logicalName]
      const fieldMap: Record<string, string> = {}
      for (const fd of s?.fields?.fields ?? []) {
        const chosen = s?.fieldMap[fd.essField]
        if (chosen && chosen !== fd.defaultNavField) fieldMap[fd.essField] = chosen
      }
      const modified = f.logicalName === 'Employee' ? (s?.fieldMap.LastModified ?? null) : null
      return { logicalName: f.logicalName, protocol: s?.service?.protocol ?? 'OData', serviceName: s?.service?.name ?? '', method: null,
        modifiedField: modified, fieldMapJson: Object.keys(fieldMap).length ? JSON.stringify(fieldMap) : null, isEnabled: !!s?.service }
    })
    for (const m of gateway?.methods ?? [])
      rows.push({ logicalName: m.logicalName, protocol: 'Soap', serviceName: gateway?.serviceName ?? '', method: m.method, modifiedField: null, fieldMapJson: null,
        isEnabled: !!gateway?.serviceName && m.available })
    return rows
  }, [discovered, selections, gateway])

  const apply = useMutation({
    mutationFn: () => navSetupApi.apply({ ...base, name: name.trim(), portalCompanyId, navCompany: company!.name, navCompanyId: company!.id, mappings, runFirstSync: runSync }),
    onMutate: () => setError(null),
    onSuccess: (r) => {
      onDone(`NAV connection saved with ${r.mappingsEnabled} feature${r.mappingsEnabled === 1 ? '' : 's'} switched on.${r.syncJobId ? ' The first sync has started.' : ''}`)
      onClose()
    },
    onError: (e) => setError(errorMessage(e)),
  })

  const steps = ['System', 'Server and sign-in', 'Company', 'What to read', 'Save']
  const employeeChosen = !!selections.Employee?.service
  const busy = connect.isPending || discover.isPending || apply.isPending || pickGateway.isPending
  const canNext = [true, !!connected?.ok, !!company && !!portalCompanyId, employeeChosen && !Object.values(selections).some((s) => s.loading), !!name.trim()][step]

  const next = () => {
    setError(null)
    if (step === 1 && !connected?.ok) return
    if (step === 2 && company) discover.mutate(company)
    if (step === 4) { apply.mutate(); return }
    setStep(step + 1)
  }

  const secretHelp = (has: boolean | undefined) => (has ? 'Leave empty to keep the stored one.' : undefined)

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="md" fullScreen={narrow}>
      <DialogTitle>{saved ? `Connection assistant: ${saved.name}` : 'Connect to NAV or Business Central'}</DialogTitle>
      <DialogContent>
        <Stepper activeStep={step} alternativeLabel sx={{ mb: 3, mt: 1 }}>{steps.map((s) => <Step key={s}><StepLabel>{narrow ? '' : s}</StepLabel></Step>)}</Stepper>
        {error && <Alert severity="error" sx={{ mb: 2, whiteSpace: 'pre-line' }}>{error}</Alert>}
        {start.isLoading && <CircularProgress />}

        {step === 0 && start.data && (
          <Stack spacing={2}>
            <Typography>Which system does the company run?</Typography>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              {([['nav', 'Dynamics NAV', 'NAV 2013 to 2018, on your own server'], ['bcOnPrem', 'Business Central on-premises', 'Business Central on your own server'],
                ['bcOnline', 'Business Central online', 'Business Central in the Microsoft cloud']] as [Family, string, string][]).map(([f, t, d]) => (
                <Card key={f} variant="outlined" sx={{ flex: 1, borderColor: family === f ? 'primary.main' : undefined, borderWidth: family === f ? 2 : 1 }}>
                  <CardActionArea onClick={() => chooseFamily(f)} sx={{ height: '100%' }}>
                    <CardContent><Typography variant="subtitle1">{t}</Typography><Typography variant="body2" color="text.secondary">{d}</Typography></CardContent>
                  </CardActionArea>
                </Card>
              ))}
            </Stack>
            {family === 'nav' && (
              <TextField select label="NAV version" value={conn.platform} onChange={(e) => set({ platform: e.target.value as NavPlatform })} sx={{ maxWidth: 280 }}
                helperText="Help, About in the NAV client shows it. NAV 2016 is version 9.">
                {navVersions.map((v) => <MenuItem key={v} value={v}>{platformNames[v]}</MenuItem>)}
              </TextField>
            )}
          </Stack>
        )}

        {step === 1 && (
          <Stack spacing={2}>
            {family !== 'bcOnline' ? (
              <>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                  <TextField fullWidth label="NAV server name" value={conn.serverHost ?? ''} onChange={(e) => set({ serverHost: e.target.value })} autoFocus
                    helperText="The computer the NAV service tier runs on, e.g. NAVSERVER or 192.168.1.20" />
                  <TextField fullWidth label="Server instance" value={conn.serviceInstance ?? ''} onChange={(e) => set({ serviceInstance: e.target.value })}
                    placeholder={defaultInstance[conn.platform]} helperText={`Leave empty for the default (${defaultInstance[conn.platform]})`} />
                </Stack>
                <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap">
                  <FormControlLabel control={<Checkbox checked={conn.https} onChange={(e) => set({ https: e.target.checked })} />} label="The NAV web services use HTTPS" />
                  {conn.https && <FormControlLabel control={<Checkbox checked={conn.validateServerCertificate} onChange={(e) => set({ validateServerCertificate: e.target.checked })} />}
                    label="Check the NAV server's certificate" />}
                  <Link component="button" type="button" onClick={() => setAdvanced(!advanced)}>{advanced ? 'Hide ports' : 'Different ports?'}</Link>
                </Stack>
                <Collapse in={advanced}>
                  <Stack direction="row" spacing={2}>
                    <TextField label="SOAP port" type="number" value={conn.soapPort ?? ''} placeholder="7047" onChange={(e) => set({ soapPort: e.target.value ? Number(e.target.value) : null })} />
                    <TextField label="OData port" type="number" value={conn.oDataPort ?? ''} placeholder="7048" onChange={(e) => set({ oDataPort: e.target.value ? Number(e.target.value) : null })} />
                  </Stack>
                </Collapse>
              </>
            ) : (
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField fullWidth label="Tenant ID or domain" value={conn.tenantId ?? ''} onChange={(e) => set({ tenantId: e.target.value })} autoFocus helperText="e.g. contoso.onmicrosoft.com" />
                <TextField fullWidth label="Environment" value={conn.environmentName ?? ''} onChange={(e) => set({ environmentName: e.target.value })} helperText="Production, or the sandbox's name" />
              </Stack>
            )}

            <Typography variant="subtitle1">How does the portal sign in?</Typography>
            <RadioGroup value={conn.authMethod} onChange={(e) => set({ authMethod: e.target.value as NavAuthMethod })}>
              {signIns[conn.platform].map((a) => (
                <FormControlLabel key={a} value={a} control={<Radio />} sx={{ alignItems: 'flex-start', mb: 1 }}
                  label={<Box sx={{ pt: 1 }}><Typography>{signInText[a][0]}</Typography><Typography variant="body2" color="text.secondary">{signInText[a][1]}</Typography></Box>} />
              ))}
            </RadioGroup>
            {conn.authMethod === 'Windows' && (
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField fullWidth label="Domain (optional)" value={conn.domain ?? ''} onChange={(e) => set({ domain: e.target.value })} />
                <TextField fullWidth label="Windows user (optional)" value={conn.userName ?? ''} onChange={(e) => set({ userName: e.target.value })}
                  helperText="Empty: the portal's own Windows account (the one you gave setup)" />
                {!!conn.userName && <TextField fullWidth type="password" autoComplete="new-password" label="Password" value={conn.password ?? ''}
                  onChange={(e) => set({ password: e.target.value })} helperText={secretHelp(saved?.hasPassword)} />}
              </Stack>
            )}
            {(conn.authMethod === 'NavUserPassword' || conn.authMethod === 'WebServiceAccessKey') && (
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField fullWidth label="NAV user name" value={conn.userName ?? ''} onChange={(e) => set({ userName: e.target.value })} />
                <TextField fullWidth type="password" autoComplete="new-password" label={conn.authMethod === 'WebServiceAccessKey' ? 'Web service access key' : 'Password'}
                  value={conn.password ?? ''} onChange={(e) => set({ password: e.target.value })} helperText={secretHelp(saved?.hasPassword) ?? 'Stored encrypted; never shown again'} />
              </Stack>
            )}
            {conn.authMethod === 'OAuthClientCredentials' && (
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                {family !== 'bcOnline' && <TextField fullWidth label="Tenant ID" value={conn.tenantId ?? ''} onChange={(e) => set({ tenantId: e.target.value })} />}
                <TextField fullWidth label="Client ID" value={conn.clientId ?? ''} onChange={(e) => set({ clientId: e.target.value })} />
                <TextField fullWidth type="password" autoComplete="new-password" label="Client secret" value={conn.clientSecret ?? ''}
                  onChange={(e) => set({ clientSecret: e.target.value })} helperText={secretHelp(saved?.hasClientSecret)} />
              </Stack>
            )}
            <Box>
              <Button variant="contained" onClick={() => connect.mutate()} disabled={connect.isPending}
                startIcon={connect.isPending ? <CircularProgress size={18} color="inherit" /> : undefined}>
                {connect.isPending ? 'Testing…' : 'Test the connection'}
              </Button>
            </Box>
            {connected && (
              <Alert ref={resultRef} severity={connected.ok ? 'success' : 'warning'} icon={false}>
                <Checks checks={connected.checks} />
                {connected.addresses.oData && <Typography variant="caption" color="text.secondary" component="div">
                  OData {connected.addresses.oData}{connected.addresses.soap ? ` · SOAP ${connected.addresses.soap}` : ''}{connected.addresses.api ? ` · API ${connected.addresses.api}/api` : ''}
                </Typography>}
              </Alert>
            )}
          </Stack>
        )}

        {step === 2 && connected && start.data && (
          <Stack spacing={2}>
            <Typography>Which NAV company holds the employees?</Typography>
            <RadioGroup value={company?.name ?? ''} onChange={(e) => setCompany(connected.companies.find((c) => c.name === e.target.value) ?? null)}>
              {connected.companies.map((c) => <FormControlLabel key={c.name} value={c.name} control={<Radio />} label={c.displayName} />)}
            </RadioGroup>
            {start.data.portalCompanies.length > 1 && (
              <TextField select label="Portal company it feeds" value={portalCompanyId} onChange={(e) => setPortalCompanyId(e.target.value)} sx={{ maxWidth: 360 }}>
                {start.data.portalCompanies.map((p) => <MenuItem key={p.id} value={p.id}>{p.displayName} ({p.code})</MenuItem>)}
              </TextField>
            )}
          </Stack>
        )}

        {step === 3 && (
          discover.isPending || !discovered ? <Stack direction="row" spacing={2} alignItems="center"><CircularProgress size={24} /><Typography>Looking at what NAV publishes…</Typography></Stack> : (
            <Stack spacing={2}>
              {discovered.checks.some((c) => !c.ok) && <Alert severity="warning" icon={false}><Checks checks={discovered.checks} /></Alert>}
              <Typography variant="body2" color="text.secondary">
                For each part of the portal, pick the NAV page that has the data. Suggestions are by name, so check them. Parts set to "Not used" stay off;
                you can switch them on later by running the assistant again. Publish pages in NAV under Web Services (see the NAV setup guide).
              </Typography>
              <Table size="small">
                <TableHead><TableRow><TableCell>Portal feature</TableCell><TableCell sx={{ minWidth: 220 }}>NAV service</TableCell><TableCell /></TableRow></TableHead>
                <TableBody>
                  {discovered.features.map((f) => {
                    const s = selections[f.logicalName]
                    const reads = discovered.services.filter((x) => x.protocol !== 'Soap')
                    const options = [...f.candidates, ...reads.filter((x) => !f.candidates.some((c) => c.name === x.name && c.protocol === x.protocol))]
                    const key = (x: NavPublishedService | null | undefined) => (x ? `${x.protocol}:${x.name}` : '')
                    const missingRequired = s?.fields?.fields.filter((fd) => fd.required && !s.fieldMap[fd.essField]) ?? []
                    const matched = s?.fields?.fields.filter((fd) => s.fieldMap[fd.essField] && s.fieldMap[fd.essField] !== fd.defaultNavField).length ?? 0
                    const status = !s?.service ? 'missing' : s.service.name === f.service?.name ? f.status : 'suggested'
                    return [
                      <TableRow key={f.logicalName}>
                        <TableCell>
                          <Typography variant="body2" fontWeight={600}>{f.label}{f.required ? ' *' : ''}</Typography>
                          <Typography variant="caption" color="text.secondary">{f.description}</Typography>
                        </TableCell>
                        <TableCell>
                          <TextField select size="small" fullWidth value={key(s?.service)} SelectProps={{ displayEmpty: true }}
                            onChange={(e) => company && loadFields(f.logicalName, options.find((o) => key(o) === e.target.value) ?? null, company)}>
                            <MenuItem value="">{f.required ? 'Choose…' : 'Not used'}</MenuItem>
                            {options.map((o) => <MenuItem key={key(o)} value={key(o)}>{o.name}{o.protocol === 'BcApi' ? ' (API)' : ''}</MenuItem>)}
                          </TextField>
                        </TableCell>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>
                          {s?.loading ? <CircularProgress size={18} /> : <Stack direction="row" spacing={1} alignItems="center">
                            {s?.service ? <Chip size="small" color={statusChip[status].color} label={statusChip[status].label} />
                              : <Chip size="small" label={f.required ? 'Required' : 'Off'} color={f.required ? 'warning' : 'default'} />}
                            {missingRequired.length > 0 && <Chip size="small" color="warning" label={`${missingRequired.length} key field missing`} />}
                            {s?.fields?.ok && <Button size="small" onClick={() => setOpenFields(openFields === f.logicalName ? null : f.logicalName)}>
                              Fields{matched ? ` (${matched} matched)` : ''}
                            </Button>}
                          </Stack>}
                        </TableCell>
                      </TableRow>,
                      s?.error && <TableRow key={f.logicalName + '-e'}><TableCell colSpan={3}><Alert severity="warning">{s.error}</Alert></TableCell></TableRow>,
                      openFields === f.logicalName && s?.fields?.ok && (
                        <TableRow key={f.logicalName + '-f'}><TableCell colSpan={3} sx={{ bgcolor: 'action.hover' }}>
                          <Typography variant="body2" sx={{ mb: 1 }}>Which NAV field holds each value? Matches by name are filled in; empty means the portal leaves it blank.</Typography>
                          <Stack spacing={1}>
                            {s.fields.fields.map((fd) => (
                              <Stack key={fd.essField} direction="row" spacing={2} alignItems="center">
                                <Typography variant="body2" sx={{ width: 180, flexShrink: 0 }}>{fd.label}{fd.required ? ' *' : ''}</Typography>
                                <TextField select size="small" sx={{ minWidth: 240 }} value={s.fieldMap[fd.essField] ?? ''} SelectProps={{ displayEmpty: true }}
                                  error={fd.required && !s.fieldMap[fd.essField]}
                                  onChange={(e) => setSelections((all) => ({ ...all, [f.logicalName]: { ...s, fieldMap: { ...s.fieldMap, [fd.essField]: e.target.value || null } } }))}>
                                  <MenuItem value="">(not in NAV)</MenuItem>
                                  {s.fields!.navFields.map((n) => <MenuItem key={n} value={n}>{n}</MenuItem>)}
                                </TextField>
                                {fd.source === 'matched' && s.fieldMap[fd.essField] === fd.navField && <Chip size="small" variant="outlined" label="matched by name" />}
                              </Stack>
                            ))}
                          </Stack>
                        </TableCell></TableRow>
                      ),
                    ]
                  })}
                </TableBody>
              </Table>

              <Card variant="outlined"><CardContent>
                <Typography variant="subtitle1">Submitting to NAV (leave, claims, profile changes)</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>This uses the ESS Portal Gateway codeunit published as a SOAP web service.</Typography>
                {gateway && (
                  <Stack spacing={1}>
                    {gateway.candidates.length > 0 && (
                      <TextField select size="small" label="Gateway codeunit" value={gateway.serviceName ?? ''} SelectProps={{ displayEmpty: true }} InputLabelProps={{ shrink: true }} sx={{ maxWidth: 360 }}
                        onChange={(e) => company && (e.target.value
                          ? pickGateway.mutate({ c: company, name: e.target.value, list: gateway.candidates })
                          : setGateway({ ...gateway, serviceName: null, methods: gateway.methods.map((m) => ({ ...m, available: false })) }))}>
                        <MenuItem value="">Not used</MenuItem>
                        {gateway.candidates.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
                      </TextField>
                    )}
                    {gateway.message && <Alert severity="info">{gateway.message}</Alert>}
                    {gateway.serviceName && <Stack direction="row" flexWrap="wrap" gap={1}>
                      {gateway.methods.map((m) => <Chip key={m.method} size="small" color={m.available ? 'success' : 'default'} variant={m.available ? 'filled' : 'outlined'}
                        label={`${m.label}${m.available ? '' : ': missing'}`} />)}
                    </Stack>}
                  </Stack>
                )}
              </CardContent></Card>
            </Stack>
          )
        )}

        {step === 4 && company && (
          <Stack spacing={2}>
            <TextField label="Name for this connection" value={name} onChange={(e) => setName(e.target.value)} sx={{ maxWidth: 360 }} helperText="For example NAV 2016 Live" />
            <Typography>
              The portal will read <b>{mappings.filter((m) => m.isEnabled && m.protocol !== 'Soap').map((m) => discovered?.features.find((f) => f.logicalName === m.logicalName)?.label).join(', ')}</b> from
              {' '}<b>{company.displayName}</b>
              {mappings.some((m) => m.isEnabled && m.protocol === 'Soap') ? ` and submit through ${gateway?.serviceName}.` : '. Submitting to NAV stays off until the gateway codeunit is published.'}
            </Typography>
            <FormControlLabel control={<Checkbox checked={runSync} onChange={(e) => setRunSync(e.target.checked)} />}
              label="Start the first full sync now (employees, departments, leave types; it runs in the background)" />
            <Typography variant="body2" color="text.secondary">
              Afterwards, follow it on <Link component={RouterLink} to="/admin/integration" onClick={onClose}>NAV integration</Link>, then link people under Users with Create accounts from NAV.
            </Typography>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>Cancel</Button>
        {step > 0 && <Button onClick={() => { setError(null); setStep(step - 1) }} disabled={busy}>Back</Button>}
        <Button variant="contained" onClick={next} disabled={!canNext || busy || !start.data}>
          {step === 4 ? (apply.isPending ? 'Saving…' : 'Save and connect') : 'Next'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
