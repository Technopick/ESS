import { useState, type ReactNode } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import {
  AppBar, Box, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, ListSubheader,
  Menu, MenuItem, Toolbar, Tooltip, useMediaQuery, useTheme,
} from '@mui/material'
import MenuIcon from '@mui/icons-material/Menu'
import HomeOutlinedIcon from '@mui/icons-material/HomeOutlined'
import PaletteOutlinedIcon from '@mui/icons-material/PaletteOutlined'
import SyncAltIcon from '@mui/icons-material/SyncAlt'
import DarkModeOutlinedIcon from '@mui/icons-material/DarkModeOutlined'
import LightModeOutlinedIcon from '@mui/icons-material/LightModeOutlined'
import AccountCircleOutlinedIcon from '@mui/icons-material/AccountCircleOutlined'
import BadgeOutlinedIcon from '@mui/icons-material/BadgeOutlined'
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'
import ManageAccountsOutlinedIcon from '@mui/icons-material/ManageAccountsOutlined'
import AdminPanelSettingsOutlinedIcon from '@mui/icons-material/AdminPanelSettingsOutlined'
import BeachAccessOutlinedIcon from '@mui/icons-material/BeachAccessOutlined'
import ReceiptLongOutlinedIcon from '@mui/icons-material/ReceiptLongOutlined'
import PaymentsOutlinedIcon from '@mui/icons-material/PaymentsOutlined'
import FolderOutlinedIcon from '@mui/icons-material/FolderOutlined'
import SupportAgentOutlinedIcon from '@mui/icons-material/SupportAgentOutlined'
import AccessTimeOutlinedIcon from '@mui/icons-material/AccessTimeOutlined'
import SchoolOutlinedIcon from '@mui/icons-material/SchoolOutlined'
import TaskAltOutlinedIcon from '@mui/icons-material/TaskAltOutlined'
import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined'
import PublishOutlinedIcon from '@mui/icons-material/PublishOutlined'
import InsightsOutlinedIcon from '@mui/icons-material/InsightsOutlined'
import AssessmentOutlinedIcon from '@mui/icons-material/AssessmentOutlined'
import PeopleAltOutlinedIcon from '@mui/icons-material/PeopleAltOutlined'
import ApartmentOutlinedIcon from '@mui/icons-material/ApartmentOutlined'
import EventNoteOutlinedIcon from '@mui/icons-material/EventNoteOutlined'
import FolderSharedOutlinedIcon from '@mui/icons-material/FolderSharedOutlined'
import CampaignOutlinedIcon from '@mui/icons-material/CampaignOutlined'
import NotificationsActiveOutlinedIcon from '@mui/icons-material/NotificationsActiveOutlined'
import SettingsEthernetOutlinedIcon from '@mui/icons-material/SettingsEthernetOutlined'
import { Permissions, reportPermissions, useAuth } from '../auth/AuthContext'
import { useThemeMode } from '../theme/AppThemeProvider'
import { brandTint } from '../theme/buildTheme'
import { BrandMark } from '../components/BrandMark'
import { NotificationBell } from '../components/NotificationBell'

const drawerWidth = 260

interface NavItem {
  to: string
  label: string
  icon: ReactNode
  permission?: string
  /** Shown when the user holds any of these permissions. */
  anyOf?: string[]
}

/** Navigation grouped by portal area (employee, manager, HR, management, administration). Items appear only when built and permitted. */
const sections: { title: string; items: NavItem[] }[] = [
  {
    title: 'My workspace',
    items: [
      { to: '/', label: 'Home', icon: <HomeOutlinedIcon /> },
      { to: '/profile', label: 'My profile', icon: <BadgeOutlinedIcon />, permission: Permissions.ProfileViewOwn },
      { to: '/leave', label: 'Leave', icon: <BeachAccessOutlinedIcon />, permission: Permissions.LeaveViewOwn },
      { to: '/payslips', label: 'Payslips', icon: <ReceiptLongOutlinedIcon />, permission: Permissions.PayrollViewOwn },
      { to: '/expenses', label: 'Expense claims', icon: <PaymentsOutlinedIcon />, permission: Permissions.ExpenseSubmit },
      { to: '/attendance', label: 'Attendance', icon: <AccessTimeOutlinedIcon />, permission: Permissions.AttendanceClock },
      { to: '/training', label: 'Training', icon: <SchoolOutlinedIcon />, permission: Permissions.TrainingApply },
      { to: '/requests', label: 'HR requests', icon: <SupportAgentOutlinedIcon />, permission: Permissions.RequestsSubmit },
      { to: '/documents', label: 'Documents', icon: <FolderOutlinedIcon />, permission: Permissions.DocumentsViewOwn },
      { to: '/directory', label: 'Directory', icon: <GroupsOutlinedIcon />, permission: Permissions.EmployeesViewDirectory },
      { to: '/account/security', label: 'Sign-in and security', icon: <LockOutlinedIcon /> },
    ],
  },
  {
    title: 'Approvals',
    items: [
      { to: '/approvals', label: 'Approvals', icon: <TaskAltOutlinedIcon />, permission: Permissions.ApprovalsAct },
    ],
  },
  {
    title: 'Management',
    items: [
      { to: '/management', label: 'Dashboard', icon: <InsightsOutlinedIcon />, permission: Permissions.DashboardManagement },
      { to: '/reports', label: 'Reports', icon: <AssessmentOutlinedIcon />, anyOf: reportPermissions },
    ],
  },
  {
    title: 'HR administration',
    items: [
      { to: '/hr/employees', label: 'Employees', icon: <PeopleAltOutlinedIcon />, permission: Permissions.EmployeesViewAll },
      { to: '/hr/organization', label: 'Organisation', icon: <ApartmentOutlinedIcon />, permission: Permissions.EmployeesViewAll },
      { to: '/hr/leave', label: 'Leave administration', icon: <EventNoteOutlinedIcon />, permission: Permissions.LeaveViewAll },
      { to: '/hr/documents', label: 'Employee documents', icon: <FolderSharedOutlinedIcon />, permission: Permissions.DocumentsManage },
      { to: '/hr/announcements', label: 'Announcements', icon: <CampaignOutlinedIcon />, permission: Permissions.AnnouncementsManage },
      { to: '/admin/payroll', label: 'Publish payslips', icon: <PublishOutlinedIcon />, permission: Permissions.PayrollPublish },
      { to: '/admin/workflows', label: 'Approval routes', icon: <AccountTreeOutlinedIcon />, permission: Permissions.WorkflowConfigure },
    ],
  },
  {
    title: 'System administration',
    items: [
      { to: '/admin/branding', label: 'Look and feel', icon: <PaletteOutlinedIcon />, permission: Permissions.SettingsManage },
      { to: '/admin/users', label: 'Users', icon: <ManageAccountsOutlinedIcon />, permission: Permissions.UsersManage },
      { to: '/admin/roles', label: 'Roles', icon: <AdminPanelSettingsOutlinedIcon />, permission: Permissions.UsersManage },
      { to: '/admin/notifications', label: 'Notifications', icon: <NotificationsActiveOutlinedIcon />, permission: Permissions.NotificationsConfigure },
      { to: '/admin/nav-settings', label: 'NAV connection', icon: <SettingsEthernetOutlinedIcon />, permission: Permissions.IntegrationConfigure },
      { to: '/admin/integration', label: 'NAV integration', icon: <SyncAltIcon />, permission: Permissions.IntegrationView },
    ],
  },
]

export function PortalLayout() {
  const theme = useTheme()
  const desktop = useMediaQuery(theme.breakpoints.up('md'))
  const [open, setOpen] = useState(false)
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const { me, can, logout } = useAuth()
  const { mode, setMode } = useThemeMode()
  const navigate = useNavigate()
  const dark = theme.palette.mode === 'dark'

  const drawer = (
    <Box role="navigation" aria-label="Main">
      <Toolbar>
        <BrandMark />
      </Toolbar>
      <Divider />
      {sections.map((s) => {
        const visible = s.items.filter((i) => (!i.permission || can(i.permission)) && (!i.anyOf || i.anyOf.some((p) => can(p))))
        if (visible.length === 0) return null
        return (
          <List key={s.title} subheader={<ListSubheader disableSticky>{s.title}</ListSubheader>} dense>
            {visible.map((i) => (
              <ListItemButton key={i.to} component={NavLink} to={i.to} end onClick={() => setOpen(false)}
                sx={{ mx: 1, borderRadius: 2, '&.active': { bgcolor: brandTint(theme), color: 'primary.main', '& .MuiListItemIcon-root': { color: 'primary.main' }, '& .MuiListItemText-primary': { fontWeight: 600 } } }}>
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
          <NotificationBell />
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
