import { CssBaseline, ThemeProvider, useMediaQuery } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { brandingApi, type BrandingView } from '../api/branding'
import { buildTheme, defaultBranding } from './buildTheme'

export type { BrandingView as Branding } from '../api/branding'

type Mode = 'light' | 'dark' | 'system'
const ModeContext = createContext<{ mode: Mode; setMode: (m: Mode) => void; branding: BrandingView }>({
  mode: 'system',
  setMode: () => {},
  branding: defaultBranding,
})

const CACHE = 'ess.branding'
const MODE = 'ess.theme'

function cached(): BrandingView {
  try {
    const s = localStorage.getItem(CACHE)
    return s ? { ...defaultBranding, ...(JSON.parse(s) as BrandingView) } : defaultBranding
  } catch {
    return defaultBranding
  }
}

/**
 * Applies the company's look and feel (Look and feel page) to the whole portal. The last branding seen is kept in the
 * browser so the next visit starts in the right colours. Each user's own light/dark choice wins over the company default.
 */
export function AppThemeProvider({ children }: { children: ReactNode }) {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)')
  const query = useQuery({ queryKey: ['branding'], queryFn: brandingApi.get, staleTime: 5 * 60_000, placeholderData: cached })
  const branding = query.data ?? defaultBranding
  const [userMode, setUserMode] = useState<Mode | null>(() => localStorage.getItem(MODE) as Mode | null)
  const mode = userMode ?? branding.defaultMode
  const dark = mode === 'dark' || (mode === 'system' && prefersDark)
  const setMode = (m: Mode) => {
    setUserMode(m)
    localStorage.setItem(MODE, m)
  }

  useEffect(() => {
    if (query.data) localStorage.setItem(CACHE, JSON.stringify(query.data))
  }, [query.data])

  const theme = useMemo(() => buildTheme(branding, dark), [branding, dark])

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    document.title = branding.appName
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme.palette.mode === 'dark' ? theme.palette.background.paper : branding.primary)
    const icon = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (icon && branding.logoUrl) icon.href = branding.logoUrl
  }, [dark, branding, theme])

  return (
    <ModeContext.Provider value={{ mode, setMode, branding }}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </ModeContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export const useThemeMode = () => useContext(ModeContext)
