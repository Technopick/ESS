import { useEffect, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import {
  Card, CardContent, InputAdornment, MenuItem, Pagination, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import SearchIcon from '@mui/icons-material/Search'
import { essApi } from '../api/types'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { Permissions, useAuth } from '../auth/AuthContext'

export function DirectoryPage() {
  const { me, can } = useAuth()
  const full = can(Permissions.EmployeesViewAll)
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [page, setPage] = useState(1)
  useEffect(() => {
    const t = setTimeout(() => { setDebounced(search); setPage(1) }, 300)
    return () => clearTimeout(t)
  }, [search])

  const depts = useQuery({ queryKey: ['departments', me?.companyId], queryFn: () => essApi.departments(me!.companyId!), enabled: !!me?.companyId })
  const q = useQuery({
    queryKey: ['directory', debounced, departmentId, page],
    queryFn: () => essApi.directory({ search: debounced || undefined, departmentId: departmentId || undefined, page }),
    placeholderData: keepPreviousData,
  })

  return (
    <Stack spacing={3}>
      <Typography variant="h1">Employee directory</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <TextField placeholder="Search by name or email" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ flex: 1 }}
          InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> }} inputProps={{ 'aria-label': 'Search employees' }} />
        <TextField select label="Department" value={departmentId} onChange={(e) => { setDepartmentId(e.target.value); setPage(1) }} sx={{ minWidth: 220 }}>
          <MenuItem value="">All departments</MenuItem>
          {(depts.data ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
        </TextField>
      </Stack>
      <Card>
        <CardContent>
          {q.isLoading ? <LoadingState /> : q.isError ? <ErrorState error={q.error} onRetry={() => q.refetch()} /> : q.data!.items.length === 0 ? (
            <EmptyState title="No employees match your search" />
          ) : (
            <>
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      {full && <TableCell>No.</TableCell>}
                      <TableCell>Name</TableCell><TableCell>Job title</TableCell><TableCell>Department</TableCell><TableCell>Location</TableCell><TableCell>Email</TableCell>
                      {full && <><TableCell>Manager</TableCell><TableCell>Status</TableCell><TableCell>Joined</TableCell></>}
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {q.data!.items.map((e) => (
                      <TableRow key={e.id} hover>
                        {full && <TableCell>{e.employeeNo}</TableCell>}
                        <TableCell>{e.fullName}</TableCell><TableCell>{e.jobTitle ?? '–'}</TableCell><TableCell>{e.department ?? '–'}</TableCell>
                        <TableCell>{e.location ?? '–'}</TableCell>
                        <TableCell>{e.workEmail ? <a href={`mailto:${e.workEmail}`}>{e.workEmail}</a> : '–'}</TableCell>
                        {full && <><TableCell>{e.managerName ?? '–'}</TableCell><TableCell>{e.status}</TableCell><TableCell>{e.employmentDate ? dayjs(e.employmentDate).format('D MMM YYYY') : '–'}</TableCell></>}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mt: 2 }}>
                <Typography variant="body2" color="text.secondary">{q.data!.totalCount} employees</Typography>
                <Pagination count={Math.max(1, Math.ceil(q.data!.totalCount / q.data!.pageSize))} page={page} onChange={(_, p) => setPage(p)} />
              </Stack>
            </>
          )}
        </CardContent>
      </Card>
    </Stack>
  )
}
