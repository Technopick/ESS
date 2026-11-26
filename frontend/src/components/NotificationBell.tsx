import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import { Badge, Box, Button, Divider, IconButton, ListItemText, Menu, MenuItem, Tooltip, Typography } from '@mui/material'
import NotificationsNoneOutlinedIcon from '@mui/icons-material/NotificationsNoneOutlined'
import { notificationsApi, type NotificationItem } from '../api/modules'
import { refreshAccessToken, tokenStore } from '../api/client'

dayjs.extend(relativeTime)

/** Lists a notification may have changed, so the page the user is on refreshes by itself. Event codes match NotificationEvents on the server. */
const affected = (code: string): string[][] => {
  const keys: string[][] = [['notifications'], ['unread-count'], ['dashboard']]
  if (code.startsWith('approval.')) keys.push(['inbox'], ['monitor'])
  if (code.startsWith('request.') || code.startsWith('nav.')) {
    keys.push(['leave-mine'], ['leave-balances'], ['expenses-mine'], ['requests-mine'], ['training-mine'], ['attendance-requests'], ['decided'])
  }
  if (code === 'payslip.available') keys.push(['payroll-periods'])
  if (code === 'document.expiring') keys.push(['documents-mine'])
  return keys
}

export function NotificationBell() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const count = useQuery({ queryKey: ['unread-count'], queryFn: notificationsApi.unreadCount, refetchInterval: 120_000 })
  const list = useQuery({ queryKey: ['notifications', 'menu'], queryFn: () => notificationsApi.list(1), enabled: !!anchor })
  const read = useMutation({ mutationFn: notificationsApi.read, onSuccess: () => qc.invalidateQueries({ queryKey: ['unread-count'] }) })
  const readAll = useMutation({ mutationFn: notificationsApi.readAll, onSuccess: () => { qc.invalidateQueries({ queryKey: ['unread-count'] }); qc.invalidateQueries({ queryKey: ['notifications'] }) } })

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', { accessTokenFactory: async () => tokenStore.get() ?? (await refreshAccessToken()) ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('notification', (n: { eventCode?: string }) => affected(n?.eventCode ?? '').forEach((k) => qc.invalidateQueries({ queryKey: k })))
    connection.start().catch(() => { /* falls back to polling the unread count */ })
    return () => { void connection.stop() }
  }, [qc])

  const open = (n: NotificationItem) => {
    if (!n.read) read.mutate(n.id)
    setAnchor(null)
    if (n.linkUrl?.startsWith('/')) navigate(n.linkUrl)
  }
  const unread = count.data?.count ?? 0
  return (
    <>
      <Tooltip title="Notifications">
        <IconButton aria-label={unread ? `Notifications, ${unread} unread` : 'Notifications'} onClick={(e) => setAnchor(e.currentTarget)}>
          <Badge color="error" badgeContent={unread} max={99}><NotificationsNoneOutlinedIcon /></Badge>
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)} slotProps={{ paper: { sx: { width: 380, maxHeight: 480 } } }}>
        <Box className="flex items-center justify-between" sx={{ px: 2, py: 1 }}>
          <Typography fontWeight={700}>Notifications</Typography>
          <Button size="small" disabled={!unread} onClick={() => readAll.mutate()}>Mark all read</Button>
        </Box>
        <Divider />
        {list.isLoading && <MenuItem disabled>Loading…</MenuItem>}
        {list.data?.items.length === 0 && <MenuItem disabled>You are all caught up.</MenuItem>}
        {list.data?.items.map((n) => (
          <MenuItem key={n.id} onClick={() => open(n)} sx={{ whiteSpace: 'normal', alignItems: 'flex-start', bgcolor: n.read ? undefined : 'action.hover' }}>
            <ListItemText primary={n.subject ?? n.body} secondary={<>{n.subject && <span style={{ display: 'block' }}>{n.body}</span>}{dayjs(n.createdAtUtc).fromNow()}</>}
              primaryTypographyProps={{ fontWeight: n.read ? 400 : 600, variant: 'body2' }} />
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
