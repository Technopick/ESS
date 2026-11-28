import { api } from './client'

export type DefaultMode = 'light' | 'dark' | 'system'
export interface BrandingSettings { appName: string; welcomeText: string | null; primary: string; secondary: string; defaultMode: DefaultMode; cornerRadius: number }
export interface BrandingView extends BrandingSettings { logoUrl: string | null }

export const brandingApi = {
  get: () => api.get<BrandingView>('/branding').then((r) => r.data),
  save: (b: BrandingSettings) => api.put<BrandingView>('/branding', b).then((r) => r.data),
  uploadLogo: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api.post<BrandingView>('/branding/logo', form).then((r) => r.data)
  },
  removeLogo: () => api.delete<BrandingView>('/branding/logo').then((r) => r.data),
}
