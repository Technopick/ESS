import { useState, type ReactNode } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import {
  AppBar, Box, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, ListSubheader,
  Menu, MenuItem, Toolbar, Tooltip, Typography, useMediaQuery, useTheme,
} from '@mui/material'
import MenuIcon from '@mui/icons-material/Menu'
import HomeOutlinedIcon from '@mui/icons-material/HomeOutlined'
import SyncAltIcon from '@mui/icons-material/SyncAlt'
import DarkModeOutlinedIcon from '@mui/icons-material/DarkModeOutlined'
import LightModeOutlinedIcon from '@mui/icons-material/LightModeOutlined'
import AccountCircleOutlinedIcon from '@mui/icons-material/AccountCircleOutlined'
import { Permissions, useAuth } from '../auth/AuthContext'
import { useThemeMode } from '../theme/AppThemeProvider'

const drawerWidth = 260

interface NavItem {
  to: string
  label: string
  icon: ReactNode
  permission?: string
}

/** Navigation grouped by portal area (employee, manager, HR, management, administration). Items appear only when built and permitted. */
const sections: { title: string; items: NavItem[] }[] = [
  { title: 'My workspace', items: [{ to: '/', label: 'Home', icon: <HomeOutlinedIcon /> }] },
  {
    title: 'System administration',
    items: [{ to: '/admin/integration', label: 'NAV integration', icon: <SyncAltIcon />, permission: Permissions.IntegrationView }],
  },
]

export function PortalLayout() {
  const theme = useTheme()
  const desktop = useMediaQuery(theme.breakpoints.up('md'))
  const [open, setOpen] = useState(false)
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const { me, can, logout } = useAuth()
  const { mode, setMode, branding } = useThemeMode()
  const navigate = useNavigate()
  const dark = theme.palette.mode === 'dark'

  const drawer = (
    <Box role="navigation" aria-label="Main">
      <Toolbar>
        <Typography variant="subtitle1" fontWeight={700} noWrap>
          {branding.companyName}
        </Typography>
      </Toolbar>
      <Divider />
      {sections.map((s) => {
        const visible = s.items.filter((i) => !i.permission || can(i.permission))
        if (visible.length === 0) return null
        return (
          <List key={s.title} subheader={<ListSubheader disableSticky>{s.title}</ListSubheader>} dense>
            {visible.map((i) => (
              <ListItemButton key={i.to} component={NavLink} to={i.to} end onClick={() => setOpen(false)}
                sx={{ mx: 1, borderRadius: 2, '&.active': { bgcolor: 'action.selected', fontWeight: 600 } }}>
                <ListItemIcon sx={{ minWidth: 36 }}>{i.icon}</ListItemIcon>
                <ListItemText primary={i.label} />
              </ListItemButton>
            ))}
          </List>
        )
      })}
    </Box>
  )

  return (
    <Box className="flex min-h-full">
      <AppBar position="fixed" color="inherit" elevation={0}
        sx={{ borderBottom: 1, borderColor: 'divider', width: { md: `calc(100% - ${drawerWidth}px)` }, ml: { md: `${drawerWidth}px` } }}>
        <Toolbar>
          {!desktop && (
            <IconButton edge="start" aria-label="Open navigation" onClick={() => setOpen(true)} sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
          )}
          <Box className="flex-1" />
          <Tooltip title={dark ? 'Switch to light mode' : 'Switch to dark mode'}>
            <IconButton aria-label="Toggle colour mode" onClick={() => setMode(dark ? 'light' : 'dark')}>
              {dark ? <LightModeOutlinedIcon /> : <DarkModeOutlinedIcon />}
            </IconButton>
          </Tooltip>
          <IconButton aria-label="Account" onClick={(e) => setAnchor(e.currentTarget)}>
            <AccountCircleOutlinedIcon />
          </IconButton>
          <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
            <MenuItem disabled>{me?.userName}</MenuItem>
            <MenuItem onClick={() => { setMode('system'); setAnchor(null) }} selected={mode === 'system'}>Use system theme</MenuItem>
            <MenuItem onClick={async () => { setAnchor(null); await logout(); navigate('/login') }}>Sign out</MenuItem>
          </Menu>
        </Toolbar>
      </AppBar>
      <Box component="nav" sx={{ width: { md: drawerWidth }, flexShrink: { md: 0 } }}>
        <Drawer variant={desktop ? 'permanent' : 'temporary'} open={desktop || open} onClose={() => setOpen(false)}
          ModalProps={{ keepMounted: true }} sx={{ '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box' } }}>
          {drawer}
        </Drawer>
      </Box>
      <Box component="main" className="flex-1 min-w-0" sx={{ p: { xs: 2, md: 3 }, mt: 8 }}>
        <Outlet />
      </Box>
    </Box>
  )
}
