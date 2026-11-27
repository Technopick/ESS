import { useQuery } from '@tanstack/react-query'
import { MenuItem, TextField } from '@mui/material'
import { lookupsApi } from '../api/admin'
import { useAuth } from '../auth/AuthContext'

/** Department and location pickers fed from the synced NAV reference data. */
export function DepartmentSelect({ value, onChange, label = 'Department', allLabel = 'All departments' }: { value: string; onChange: (v: string) => void; label?: string; allLabel?: string }) {
  const { me } = useAuth()
  const q = useQuery({ queryKey: ['lookup-departments', me?.companyId], queryFn: () => lookupsApi.departments(me!.companyId!), enabled: !!me?.companyId, staleTime: 600_000 })
  return (
    <TextField select size="small" label={label} value={value} onChange={(e) => onChange(e.target.value)} sx={{ minWidth: 180 }}>
      <MenuItem value="">{allLabel}</MenuItem>
      {(q.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
    </TextField>
  )
}

export function LocationSelect({ value, onChange, label = 'Location', allLabel = 'All locations' }: { value: string; onChange: (v: string) => void; label?: string; allLabel?: string }) {
  const { me } = useAuth()
  const q = useQuery({ queryKey: ['lookup-locations', me?.companyId], queryFn: () => lookupsApi.locations(me!.companyId!), enabled: !!me?.companyId, staleTime: 600_000 })
  return (
    <TextField select size="small" label={label} value={value} onChange={(e) => onChange(e.target.value)} sx={{ minWidth: 160 }}>
      <MenuItem value="">{allLabel}</MenuItem>
      {(q.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
    </TextField>
  )
}
