import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Autocomplete, TextField } from '@mui/material'
import { hrEmployeesApi } from '../api/admin'

/** Searches current employees within the HR user's scope. */
export function EmployeePicker({ value, valueLabel, onChange, label = 'Employee' }: { value: string | null; valueLabel?: string | null; onChange: (id: string | null, name: string | null) => void; label?: string }) {
  const [search, setSearch] = useState('')
  const q = useQuery({ queryKey: ['employee-picker', search], queryFn: () => hrEmployeesApi.list({ search, status: 'Active' }) })
  const options = (q.data?.items ?? []).map((e) => ({ id: e.id, name: e.name, hint: [e.employeeNo, e.department].filter(Boolean).join(' · ') }))
  const current = value ? options.find((o) => o.id === value) ?? { id: value, name: valueLabel ?? 'Selected employee', hint: '' } : null
  return (
    <Autocomplete size="small" options={options} value={current} getOptionLabel={(o) => o.name} isOptionEqualToValue={(a, b) => a.id === b.id}
      filterOptions={(x) => x} onInputChange={(_, v, reason) => reason === 'input' && setSearch(v)} onChange={(_, v) => onChange(v?.id ?? null, v?.name ?? null)}
      renderOption={(props, o) => <li {...props} key={o.id}>{o.name}<span style={{ marginLeft: 8, opacity: 0.6, fontSize: 12 }}>{o.hint}</span></li>}
      renderInput={(p) => <TextField {...p} label={label} />} sx={{ minWidth: 260 }} />
  )
}
