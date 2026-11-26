import { Chip, Stack, Tooltip } from '@mui/material'
import { navLabel, statusColor, statusLabel, type NavSyncStatus } from '../api/modules'

export function StatusChip({ status }: { status: string }) {
  return <Chip size="small" color={statusColor(status)} label={statusLabel[status] ?? status} />
}

/** Shows where a request stands with NAV. Nothing counts as posted until NAV confirms it. */
export function NavChip({ status, documentNo }: { status: NavSyncStatus; documentNo?: string | null }) {
  if (status === 'NotRequired') return null
  const color = status === 'Posted' ? 'success' : status === 'Failed' ? 'error' : 'default'
  return (
    <Tooltip title={documentNo ? `NAV document ${documentNo}` : ''}>
      <Chip size="small" variant="outlined" color={color} label={navLabel[status]} />
    </Tooltip>
  )
}

export function Statuses({ status, navStatus, navDocumentNo }: { status: string; navStatus?: NavSyncStatus; navDocumentNo?: string | null }) {
  return (
    <Stack direction="row" gap={0.5} flexWrap="wrap">
      <StatusChip status={status} />
      {navStatus && <NavChip status={navStatus} documentNo={navDocumentNo} />}
    </Stack>
  )
}
