import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { Link } from 'react-router-dom'
import {
  Alert, Avatar, Box, Card, CardActionArea, CardContent, CardHeader, Chip, Divider, Grid2 as Grid, LinearProgress, List, ListItem, ListItemText, Stack, Typography,
} from '@mui/material'
import { essApi } from '../api/types'
import { EmptyState, ErrorState, LoadingState } from '../components/StateViews'
import { Permissions, useAuth } from '../auth/AuthContext'

const fmt = (d: string | null) => (d ? dayjs(d).format('D MMM YYYY') : '–')
const initials = (n: string) => n.split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase()

function Counter({ label, value, to }: { label: string; value: number; to?: string }) {
  const body = (
    <CardContent>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Typography variant="h5" fontWeight={700}>{value}</Typography>
    </CardContent>
  )
  return <Card>{to ? <CardActionArea component={Link} to={to}>{body}</CardActionArea> : body}</Card>
}

export function DashboardPage() {
  const { me, can } = useAuth()
  const q = useQuery({ queryKey: ['dashboard'], queryFn: essApi.dashboard })
  if (q.isLoading) return <LoadingState rows={5} />
  if (q.isError) return <ErrorState error={q.error} onRetry={() => q.refetch()} />
  const d = q.data!
  const e = d.employee

  return (
    <Stack spacing={3}>
      {e ? (
        <Card>
          <CardContent>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }}>
              <Avatar sx={{ width: 64, height: 64, bgcolor: 'primary.main', fontSize: 24 }}>{initials(e.fullName)}</Avatar>
              <Box className="flex-1">
                <Typography variant="h1">Welcome, {e.fullName.split(' ')[0]}</Typography>
                <Typography color="text.secondary">
                  {[e.jobTitle, e.department, e.location].filter(Boolean).join(' · ') || 'Job details not set in NAV'}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Employee no. {e.employeeNo} · Reports to {e.managerName ?? '–'} · Joined {fmt(e.employmentDate)}
                </Typography>
              </Box>
              <Chip label={e.status} color={e.status === 'Active' ? 'success' : 'default'} />
            </Stack>
          </CardContent>
        </Card>
      ) : (
        <Alert severity="info">
          Welcome{me?.userName ? `, ${me.userName}` : ''}. Your account is not linked to a NAV employee record, so personal information is not shown.
        </Alert>
      )}

      <Grid container spacing={2}>
        {e && <Grid size={{ xs: 6, md: 3 }}><Counter label="My open requests" value={d.pending.myOpenRequests} /></Grid>}
        {e && <Grid size={{ xs: 6, md: 3 }}><Counter label="Leave awaiting approval" value={d.pending.myPendingLeave} /></Grid>}
        {can('approvals.act') && <Grid size={{ xs: 6, md: 3 }}><Counter label="Waiting for my approval" value={d.pending.approvalsWaitingForMe} /></Grid>}
        <Grid size={{ xs: 6, md: 3 }}><Counter label="Unread notifications" value={d.pending.unreadNotifications} /></Grid>
      </Grid>

      <Grid container spacing={2}>
        {e && can(Permissions.LeaveViewOwn) && (
          <Grid size={{ xs: 12, md: 6 }}>
            <Card sx={{ height: '100%' }}>
              <CardHeader title="Leave balances" subheader={d.leave?.retrievedAtUtc ? `From NAV, ${dayjs(d.leave.retrievedAtUtc).format('D MMM HH:mm')}` : undefined} />
              <CardContent sx={{ pt: 0 }}>
                {d.leave?.navMessage && <Alert severity={d.leave.fromCache ? 'warning' : 'info'} sx={{ mb: 2 }}>{d.leave.navMessage}</Alert>}
                {!d.leave || d.leave.balances.length === 0 ? <EmptyState title="No leave balances" /> : (
                  <Stack spacing={2}>
                    {d.leave.balances.map((b) => {
                      const total = b.entitlement + b.broughtForward
                      return (
                        <Box key={b.leaveTypeId}>
                          <Stack direction="row" justifyContent="space-between">
                            <Typography fontWeight={600}>{b.leaveType}</Typography>
                            <Typography><strong>{b.available}</strong> of {total} days left</Typography>
                          </Stack>
                          <LinearProgress variant="determinate" value={total > 0 ? Math.min(100, ((total - b.available) / total) * 100) : 0} sx={{ my: 0.5, height: 6, borderRadius: 3 }} />
                          <Typography variant="caption" color="text.secondary">Taken {b.taken} · Pending {b.pending} · Period {b.period}</Typography>
                        </Box>
                      )
                    })}
                  </Stack>
                )}
              </CardContent>
            </Card>
          </Grid>
        )}
        {e && (
          <Grid size={{ xs: 12, md: 6 }}>
            <Stack spacing={2} sx={{ height: '100%' }}>
              <Card>
                <CardHeader title="Upcoming leave" />
                <CardContent sx={{ pt: 0 }}>
                  {d.upcomingLeave.length === 0 ? <EmptyState title="No upcoming leave" /> : (
                    <List dense disablePadding>
                      {d.upcomingLeave.map((l) => (
                        <ListItem key={l.id} disableGutters secondaryAction={<Chip size="small" label={l.status} />}>
                          <ListItemText primary={`${l.leaveType}: ${fmt(l.startDate)} – ${fmt(l.endDate)}`} secondary={`${l.days} days · ${l.reference}`} />
                        </ListItem>
                      ))}
                    </List>
                  )}
                </CardContent>
              </Card>
              {can(Permissions.PayrollViewOwn) && (
                <Card>
                  <CardHeader title="Recent payslips" />
                  <CardContent sx={{ pt: 0 }}>
                    {d.recentPayslips.length === 0 ? <EmptyState title="No payslips published yet" /> : (
                      <List dense disablePadding>
                        {d.recentPayslips.map((p) => <ListItem key={p.periodId} disableGutters><ListItemText primary={p.name} secondary={`Paid ${fmt(p.payDate)}`} /></ListItem>)}
                      </List>
                    )}
                  </CardContent>
                </Card>
              )}
            </Stack>
          </Grid>
        )}
        <Grid size={{ xs: 12, md: 6 }}>
          <Card sx={{ height: '100%' }}>
            <CardHeader title="Announcements" />
            <CardContent sx={{ pt: 0 }}>
              {d.announcements.length === 0 ? <EmptyState title="No announcements" /> : d.announcements.map((a, i) => (
                <Box key={a.id}>
                  {i > 0 && <Divider sx={{ my: 1.5 }} />}
                  <Typography fontWeight={600}>{a.isPinned && '📌 '}{a.title}</Typography>
                  <Typography variant="caption" color="text.secondary">{fmt(a.publishAtUtc)}</Typography>
                  {/* Announcement HTML is sanitised server-side when HR saves it (Phase 5). Render as text until then. */}
                  <Typography variant="body2" sx={{ mt: 0.5 }}>{a.bodyHtml.replace(/<[^>]+>/g, ' ')}</Typography>
                </Box>
              ))}
            </CardContent>
          </Card>
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <Card sx={{ height: '100%' }}>
            <CardHeader title="Birthdays and anniversaries" subheader="Next 14 days" />
            <CardContent sx={{ pt: 0 }}>
              {d.celebrations.length === 0 ? <EmptyState title="Nothing in the next two weeks" /> : (
                <List dense disablePadding>
                  {d.celebrations.map((c, i) => (
                    <ListItem key={i} disableGutters>
                      <ListItemText primary={`${c.name}${c.years ? ` · ${c.years} year${c.years > 1 ? 's' : ''}` : ''}`}
                        secondary={`${c.kind} · ${dayjs(c.date).format('ddd D MMM')}${c.department ? ` · ${c.department}` : ''}`} />
                    </ListItem>
                  ))}
                </List>
              )}
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </Stack>
  )
}
