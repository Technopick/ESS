import { Avatar, Box, Stack, Typography } from '@mui/material'
import { useThemeMode } from '../theme/AppThemeProvider'

/** The company logo (or its initials on the brand colour) with the portal name. */
export function BrandMark({ size = 32, showName = true, inverse = false }: { size?: number; showName?: boolean; inverse?: boolean }) {
  const { branding } = useThemeMode()
  const initials = branding.appName.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('')
  return (
    <Stack direction="row" spacing={1.25} alignItems="center" sx={{ minWidth: 0 }}>
      {branding.logoUrl
        ? <Box component="img" src={branding.logoUrl} alt="" sx={{ height: size, maxWidth: size * 4, objectFit: 'contain', flexShrink: 0 }} />
        : <Avatar variant="rounded" sx={{ width: size, height: size, fontSize: size * 0.42, fontWeight: 700, flexShrink: 0,
            bgcolor: inverse ? 'rgba(255,255,255,0.18)' : 'primary.main', color: inverse ? '#fff' : 'primary.contrastText' }}>{initials}</Avatar>}
      {showName && <Typography variant="subtitle1" fontWeight={700} sx={{ color: inverse ? '#fff' : 'text.primary', lineHeight: 1.2, overflow: 'hidden', display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical' }}>{branding.appName}</Typography>}
    </Stack>
  )
}
