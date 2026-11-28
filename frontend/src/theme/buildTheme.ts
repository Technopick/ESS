import { alpha, createTheme, darken, lighten, type Theme } from '@mui/material'
import type { BrandingView } from '../api/branding'

export const defaultBranding: BrandingView = {
  appName: 'Employee Self-Service', welcomeText: null, primary: '#0b5394', secondary: '#38761d', defaultMode: 'system', cornerRadius: 10, logoUrl: null,
}

/** Colour presets offered on the Look and feel page. Every primary passes 4.5:1 with white text. */
export const presets: { name: string; primary: string; secondary: string }[] = [
  { name: 'Ocean', primary: '#0b5394', secondary: '#38761d' },
  { name: 'Forest', primary: '#1b5e20', secondary: '#8d6e00' },
  { name: 'Savanna', primary: '#8a4b08', secondary: '#1f5f8b' },
  { name: 'Burgundy', primary: '#8e1b3a', secondary: '#455a64' },
  { name: 'Royal', primary: '#4527a0', secondary: '#00796b' },
  { name: 'Teal', primary: '#00695c', secondary: '#ad1457' },
  { name: 'Graphite', primary: '#37474f', secondary: '#0277bd' },
  { name: 'Crimson', primary: '#b71c1c', secondary: '#283593' },
]

function luminance(hex: string) {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]
}

/** WCAG 2 contrast ratio between two #rrggbb colours. */
export function contrast(a: string, b: string) {
  const [x, y] = [luminance(a), luminance(b)].sort((p, q) => q - p)
  return (x + 0.05) / (y + 0.05)
}

export const isHex = (s: string) => /^#[0-9a-fA-F]{6}$/.test(s)

function toHex(color: string) {
  const m = color.match(/\d+(\.\d+)?/g)
  if (!m || color.startsWith('#')) return color
  return '#' + m.slice(0, 3).map((n) => Math.round(Number(n)).toString(16).padStart(2, '0')).join('')
}

/** On a dark background the brand colour is lightened until it reads well (4.5:1 against the dark paper). */
export function forDark(hex: string, background = '#121212') {
  let c = hex
  for (let i = 1; i <= 10 && contrast(c, background) < 4.5; i++) c = toHex(lighten(hex, i * 0.08))
  return c
}

export function buildTheme(b: BrandingView, dark: boolean): Theme {
  const primary = dark ? forDark(b.primary) : b.primary
  const secondary = dark ? forDark(b.secondary) : b.secondary
  const radius = b.cornerRadius
  return createTheme({
    palette: {
      mode: dark ? 'dark' : 'light',
      primary: { main: primary },
      secondary: { main: secondary },
      background: dark ? { default: '#0f1115', paper: '#171a21' } : { default: '#f5f7fa', paper: '#ffffff' },
      divider: dark ? 'rgba(255,255,255,0.10)' : 'rgba(15,23,42,0.10)',
    },
    shape: { borderRadius: radius },
    typography: {
      fontFamily: '"Inter", "Segoe UI", Roboto, Arial, sans-serif',
      h1: { fontSize: '1.75rem', fontWeight: 650, letterSpacing: '-0.01em' },
      h2: { fontSize: '1.35rem', fontWeight: 650 },
      h3: { fontSize: '1.15rem', fontWeight: 600 },
      subtitle1: { fontWeight: 600 },
      button: { textTransform: 'none', fontWeight: 600 },
    },
    components: {
      MuiCard: {
        defaultProps: { variant: 'outlined' },
        styleOverrides: { root: { borderRadius: Math.max(radius, 4) + 2 } },
      },
      MuiButton: { defaultProps: { disableElevation: true } },
      MuiAppBar: { styleOverrides: { root: { backdropFilter: 'blur(8px)' } } },
      MuiListSubheader: { styleOverrides: { root: { fontSize: '0.72rem', fontWeight: 700, letterSpacing: '0.06em', textTransform: 'uppercase', lineHeight: '36px', background: 'transparent' } } },
      MuiTableCell: { styleOverrides: { head: { fontWeight: 600 } } },
      MuiChip: { styleOverrides: { root: { fontWeight: 500 } } },
      MuiTooltip: { defaultProps: { arrow: true } },
    },
  })
}

/** Background tint for the selected navigation item and other brand accents. */
export const brandTint = (t: Theme, amount = 0.12) => alpha(t.palette.primary.main, t.palette.mode === 'dark' ? amount + 0.06 : amount)
export const brandGradient = (t: Theme) =>
  `linear-gradient(135deg, ${t.palette.primary.main} 0%, ${t.palette.mode === 'dark' ? darken(t.palette.primary.main, 0.4) : darken(t.palette.primary.main, 0.35)} 100%)`
