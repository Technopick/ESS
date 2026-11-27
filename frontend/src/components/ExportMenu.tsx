import { useState } from 'react'
import { Button, Menu, MenuItem } from '@mui/material'
import FileDownloadOutlinedIcon from '@mui/icons-material/FileDownloadOutlined'
import type { ExportFormat } from '../api/admin'

/** Export button offering Excel, PDF and (optionally) CSV. Reports the error through onError instead of failing silently. */
export function ExportMenu({ onExport, csv = true, onError, disabled }: { onExport: (f: ExportFormat) => Promise<void>; csv?: boolean; onError: (e: unknown) => void; disabled?: boolean }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const [busy, setBusy] = useState(false)
  const run = async (f: ExportFormat) => {
    setAnchor(null)
    setBusy(true)
    try { await onExport(f) } catch (e) { onError(e) } finally { setBusy(false) }
  }
  return (
    <>
      <Button variant="outlined" startIcon={<FileDownloadOutlinedIcon />} disabled={busy || disabled} onClick={(e) => setAnchor(e.currentTarget)}>
        {busy ? 'Preparing…' : 'Export'}
      </Button>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
        <MenuItem onClick={() => run('Xlsx')}>Excel (.xlsx)</MenuItem>
        <MenuItem onClick={() => run('Pdf')}>PDF</MenuItem>
        {csv && <MenuItem onClick={() => run('Csv')}>CSV</MenuItem>}
      </Menu>
    </>
  )
}
