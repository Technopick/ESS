import { api } from './client'

export type NavPlatform = 'Nav2013' | 'Nav2015' | 'Nav2016' | 'Nav2017' | 'Nav2018' | 'BusinessCentralOnPrem' | 'BusinessCentralOnline'
export type NavAuthMethod = 'Windows' | 'NavUserPassword' | 'WebServiceAccessKey' | 'OAuthClientCredentials'

export interface NavInstance {
  id: string; name: string; platform: NavPlatform; serverHost: string | null; serviceInstance: string | null
  soapBaseUrl: string | null; oDataBaseUrl: string | null; apiBaseUrl: string | null; tenantId: string | null; environmentName: string | null
  authMethod: NavAuthMethod; userName: string | null; domain: string | null; hasPassword: boolean; clientId: string | null; hasClientSecret: boolean
  timeoutSeconds: number; validateServerCertificate: boolean; maxConcurrency: number; isEnabled: boolean; useSimulator: boolean
  lastHealthCheckAtUtc: string | null; lastHealthCheckOk: boolean | null; lastHealthCheckMessage: string | null; allowedAuth: NavAuthMethod[]; endpoints: number
}
export interface NavCompanyLink { id: string; code: string; displayName: string; navCompanyName: string; navCompanyGuid: string | null; integrationSettingsId: string | null }
/** password / clientSecret: null keeps the stored value, '' clears it. */
export interface NavInstanceInput {
  name: string; platform: NavPlatform; serverHost: string | null; serviceInstance: string | null; soapBaseUrl: string | null; oDataBaseUrl: string | null
  apiBaseUrl: string | null; tenantId: string | null; environmentName: string | null; authMethod: NavAuthMethod; userName: string | null; domain: string | null
  password: string | null; clientId: string | null; clientSecret: string | null; timeoutSeconds: number; validateServerCertificate: boolean; maxConcurrency: number; isEnabled: boolean
}
export interface NavEndpoint { id: string; logicalName: string; protocol: 'OData' | 'Soap' | 'BcApi'; serviceName: string; method: string | null; modifiedField: string | null; fieldMapJson: string | null; isEnabled: boolean }

export const platformNames: Record<NavPlatform, string> = {
  Nav2013: 'NAV 2013 / 2013 R2', Nav2015: 'NAV 2015', Nav2016: 'NAV 2016', Nav2017: 'NAV 2017', Nav2018: 'NAV 2018',
  BusinessCentralOnPrem: 'Business Central on-premises', BusinessCentralOnline: 'Business Central online',
}
export const authNames: Record<NavAuthMethod, string> = {
  Windows: 'Windows (NTLM)', NavUserPassword: 'NAV user name and password', WebServiceAccessKey: 'Web service access key', OAuthClientCredentials: 'OAuth (Microsoft Entra ID)',
}

export const navSettingsApi = {
  overview: () => api.get<{ instances: NavInstance[]; companies: NavCompanyLink[] }>('/admin/nav').then((r) => r.data),
  create: (b: NavInstanceInput) => api.post<NavInstance>('/admin/nav', b).then((r) => r.data),
  update: (id: string, b: NavInstanceInput) => api.put<NavInstance>(`/admin/nav/${id}`, b).then((r) => r.data),
  suggestUrls: (platform: NavPlatform, host: string, instance: string, https: boolean) =>
    api.get<{ soapBaseUrl: string; oDataBaseUrl: string; serviceInstance: string }>('/admin/nav/suggest-urls', { params: { platform, host, instance: instance || undefined, https } }).then((r) => r.data),
  endpoints: (id: string) => api.get<{ endpoints: NavEndpoint[]; missing: string[] }>(`/admin/nav/${id}/endpoints`).then((r) => r.data),
  applyPreset: (id: string) => api.post<{ added: number }>(`/admin/nav/${id}/endpoints/preset`, {}).then((r) => r.data),
  saveEndpoint: (id: string, logicalName: string, b: Omit<NavEndpoint, 'id' | 'logicalName'>) =>
    api.put<NavEndpoint>(`/admin/nav/${id}/endpoints/${encodeURIComponent(logicalName)}`, b).then((r) => r.data),
  linkCompany: (companyId: string, b: { integrationSettingsId: string | null; navCompanyName: string; navCompanyGuid: string | null }) =>
    api.put<NavCompanyLink>(`/admin/nav/companies/${companyId}`, b).then((r) => r.data),
}

// ---------------------------------------------------------------- connection assistant
export interface NavSetupConnection {
  platform: NavPlatform; serverHost: string | null; serviceInstance: string | null; https: boolean; soapPort: number | null; oDataPort: number | null
  tenantId: string | null; environmentName: string | null; authMethod: NavAuthMethod; userName: string | null; domain: string | null; password: string | null
  clientId: string | null; clientSecret: string | null; validateServerCertificate: boolean
}
export interface NavSetupCheck { step: string; ok: boolean; message: string; hint: string | null }
export interface NavSetupCompany { name: string; displayName: string; id: string | null }
export interface NavConnectResult { ok: boolean; addresses: { soap: string | null; oData: string | null; api: string | null }; checks: NavSetupCheck[]; companies: NavSetupCompany[] }
export interface NavPublishedService { name: string; kind: string; protocol: 'OData' | 'Soap' | 'BcApi' }
export interface NavFeatureSuggestion {
  logicalName: string; label: string; description: string; required: boolean; status: 'found' | 'suggested' | 'missing'
  service: NavPublishedService | null; candidates: NavPublishedService[]; modifiedField: string | null
}
export interface NavGatewayMethod { logicalName: string; method: string; label: string; available: boolean }
export interface NavGatewaySuggestion { serviceName: string | null; candidates: string[]; methods: NavGatewayMethod[]; message: string | null }
export interface NavDiscoverResult { ok: boolean; checks: NavSetupCheck[]; services: NavPublishedService[]; features: NavFeatureSuggestion[]; gateway: NavGatewaySuggestion }
export interface NavFieldSuggestion { essField: string; label: string; required: boolean; defaultNavField: string; navField: string | null; source: 'default' | 'matched' | 'missing' }
export interface NavFieldsResult { ok: boolean; message: string | null; navFields: string[]; fields: NavFieldSuggestion[]; fieldMapJson: string | null; modifiedField: string | null }
export interface NavSavedMapping { logicalName: string; protocol: 'OData' | 'Soap' | 'BcApi'; serviceName: string; method: string | null; modifiedField: string | null; fieldMapJson: string | null; isEnabled: boolean }
export interface NavSetupStart {
  portalCompanies: { id: string; code: string; displayName: string; integrationSettingsId: string | null; navCompanyName: string; navCompanyGuid: string | null }[]
  saved: (Omit<NavSetupConnection, 'password' | 'clientSecret'> & { id: string; name: string; hasPassword: boolean; hasClientSecret: boolean; mappings: NavSavedMapping[] }) | null
}
export interface NavSetupMapping { logicalName: string; protocol: 'OData' | 'Soap' | 'BcApi'; serviceName: string; method: string | null; modifiedField: string | null; fieldMapJson: string | null; isEnabled: boolean }

type Base = { connection: NavSetupConnection; settingsId: string | null }
export const navSetupApi = {
  start: (settingsId: string | null) => api.get<NavSetupStart>('/admin/nav/setup/start', { params: { settingsId: settingsId ?? undefined } }).then((r) => r.data),
  connect: (b: Base) => api.post<NavConnectResult>('/admin/nav/setup/connect', b, { timeout: 90000 }).then((r) => r.data),
  discover: (b: Base & { company: string; companyId: string | null }) => api.post<NavDiscoverResult>('/admin/nav/setup/discover', b, { timeout: 90000 }).then((r) => r.data),
  gateway: (b: Base & { company: string; companyId: string | null; serviceName: string; codeunits: string[] }) =>
    api.post<NavGatewaySuggestion>('/admin/nav/setup/gateway', b, { timeout: 90000 }).then((r) => r.data),
  fields: (b: Base & { company: string; companyId: string | null; logicalName: string; protocol: string; serviceName: string }) =>
    api.post<NavFieldsResult>('/admin/nav/setup/fields', b, { timeout: 90000 }).then((r) => r.data),
  apply: (b: Base & { name: string; portalCompanyId: string; navCompany: string; navCompanyId: string | null; mappings: NavSetupMapping[]; runFirstSync: boolean }) =>
    api.post<{ settingsId: string; mappingsSaved: number; mappingsEnabled: number; syncJobId: string | null }>('/admin/nav/setup/apply', b).then((r) => r.data),
}
