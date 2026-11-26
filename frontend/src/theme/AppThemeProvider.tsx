import { CssBaseline, ThemeProvider, createTheme, useMediaQuery } from '@mui/material'
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

/** Company branding. Phase 5 loads these from SystemSettings; until then the defaults apply. */
export interface Branding {
  companyName: string
  primary: string
  secondary: string
  logoUrl?: string
}

export const defaultBranding: Branding = { companyName: 'Employee Self-Service', primary: '#0b5394', secondary: '#38761d' }

type Mode = 'light' | 'dark' | 'system'
const ModeContext = createContext<{ mode: Mode; setMode: (m: Mode) => void; branding: Branding }>({
  mode: 'system',
  setMode: () => {},
  branding: defaultBranding,
})

export function AppThemeProvider({ children, branding = defaultBranding }: { children: ReactNode; branding?: Branding }) {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)')
  const [mode, setMode] = useState<Mode>(() => (localStorage.getItem('ess.theme') as Mode) ?? 'system')
  const dark = mode === 'dark' || (mode === 'system' && prefersDark)

  useEffect(() => {
    localStorage.setItem('ess.theme', mode)
    document.documentElement.classList.toggle('dark', dark)
  }, [mode, dark])

  const theme = useMemo(
    () =>
      createTheme({
        palette: { mode: dark ? 'dark' : 'light', primary: { main: branding.primary }, secondary: { main: branding.secondary } },
        shape: { borderRadius: 10 },
        typography: { fontFamily: '"Inter", "Segoe UI", Roboto, Arial, sans-serif', h1: { fontSize: '1.75rem', fontWeight: 600 } },
        components: {
          MuiCard: { defaultProps: { variant: 'outlined' } },
          MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { textTransform: 'none', fontWeight: 600 } } },
        },
      }),
    [dark, branding],
  )

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
